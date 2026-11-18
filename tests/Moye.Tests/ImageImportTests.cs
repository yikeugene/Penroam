using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Moye.Controls;
using Moye.Localization;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

[Collection("Page spread shell")]
public sealed class ImageImportTests(PageSpreadTestApplication application)
{
    [Theory]
    [InlineData(20001, 1)]
    [InlineData(10000, 9000)]
    public Task OversizedImageHeaderIsRejectedBeforeDecodingPixelsOrStoringAnAsset(int width, int height)
        => application.RunAsync(async () =>
        {
            using var fixture = new ImageWindowFixture();
            var bytes = Png(1, 1);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), Crc32(bytes.AsSpan(12, 17)));
            // The pixel payload remains 1x1: a full decode would fail for a different
            // reason. The dimension-specific error proves the header guard ran first.
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.InsertAsync(bytes));
            Assert.Equal(LocalizationService.Text("Core.ImageTooLarge"), exception.Message);
            Assert.Empty(fixture.Repository.Assets);
            Assert.Empty(fixture.Window.ViewModel.Document!.Pages[0].Images);
            Assert.False(fixture.Window.ViewModel.CanUndo);
        });

    [Fact]
    public Task ImportPreservesOriginalBytesAndUsesOriginalDimensionsForPagePlacement()
        => application.RunAsync(async () =>
        {
            using var fixture = new ImageWindowFixture();
            var bytes = Png(4096, 128);
            var original = bytes.ToArray();

            await fixture.InsertAsync(bytes);

            var asset = Assert.Single(fixture.Repository.Assets.Values);
            Assert.Equal(original, asset.Bytes);
            Assert.Equal(original, bytes);
            var image = Assert.Single(fixture.Window.ViewModel.Document!.Pages[0].Images);
            Assert.Equal(asset.Id, image.AssetId);
            Assert.Equal(3072, image.Width, 6);
            Assert.Equal(96, image.Height, 6);
            Assert.True(fixture.Window.ViewModel.CanUndo);
            fixture.Window.ViewModel.Undo();
            Assert.Empty(fixture.Window.ViewModel.Document.Pages[0].Images);
        });

    [Theory]
    [InlineData(4096, 128)]
    [InlineData(128, 4096)]
    [InlineData(20000, 1)]
    [InlineData(1, 20000)]
    public Task InsertedImageReusesTheBoundedFrozenPreviewWithoutRereadingTheAsset(int width, int height)
        => application.RunAsync(async () =>
        {
            var prepared = await ImageImportPreparation.PrepareAsync(Png(width, height));
            Assert.Equal(width, prepared.PixelWidth);
            Assert.Equal(height, prepared.PixelHeight);
            Assert.True(prepared.Preview.IsFrozen);
            Assert.InRange(prepared.Preview.PixelWidth, 1, 2048);
            Assert.InRange(prepared.Preview.PixelHeight, 1, 2048);
            Assert.Equal(2048, Math.Max(prepared.Preview.PixelWidth, prepared.Preview.PixelHeight));
            var reads = 0;
            var page = new NotePage();
            var editor = new PageEditor(page, _ => { reads++; throw new InvalidOperationException("Preview should be reused."); });
            var changes = 0;
            editor.ContentChanged += (_, _) => changes++;

            editor.AddImage(new NoteImage { AssetId = "original", Width = 320, Height = 100 }, prepared.Preview);

            Assert.Equal(0, reads);
            Assert.Equal(1, changes);
            Assert.Same(prepared.Preview, Descendants<Image>(editor).Single(picture => picture.Source == prepared.Preview).Source);
        });

    [Fact]
    public async Task CanceledPreparationProducesNoPreview()
        => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImageImportPreparation.PrepareAsync([1], new CancellationToken(true)));

    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4) { pixels[offset + 1] = 170; pixels[offset + 3] = 255; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class ImageWindowFixture : IDisposable
    {
        private readonly StorageTestDirectory _directory = new();
        public ImageRepository Repository { get; } = new();
        public MainWindow Window { get; }
        public ImageWindowFixture()
        {
            Window = new MainWindow(Repository, new WritingPreferencesStore(Path.Combine(_directory.Root, "writing.json")),
                new ErrorReporter(Path.Combine(_directory.Root, "errors.log")), workspaceStore: new WorkspacePreferencesStore(Path.Combine(_directory.Root, "workspace.json")));
            Window.ViewModel.ReplaceDocument(new NotebookDocument { Pages = [new NotePage { Width = 5000, Height = 3000 }] }, true);
        }
        public Task InsertAsync(byte[] bytes) => (Task)typeof(MainWindow).GetMethod("InsertImageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Window, [bytes, "original.png", "image/png"])!;
        public void Dispose()
        {
            typeof(MainWindow).GetField("_closing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, true);
            foreach (var timer in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(DispatcherTimer)).Select(field => (DispatcherTimer?)field.GetValue(Window))) timer?.Stop();
            SystemEvents.PowerModeChanged -= (PowerModeChangedEventHandler)Delegate.CreateDelegate(typeof(PowerModeChangedEventHandler), Window,
                typeof(MainWindow).GetMethod("PowerModeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!);
            Window.ViewModel.Dispose(); Window.Close(); _directory.Dispose();
        }
    }

    private sealed class ImageRepository : INotebookRepository
    {
        public Dictionary<string, AssetData> Assets { get; } = [];
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        {
            var asset = new AssetData(Convert.ToHexStringLower(SHA256.HashData(bytes)), fileName, contentType, bytes.ToArray());
            Assets.Add(asset.Id, asset); return Task.FromResult(asset);
        }
        public Task<AssetData> GetAssetAsync(string id) => Task.FromResult(Assets[id]);
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public void Dispose() { }
    }
}
