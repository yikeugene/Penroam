using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.Tests;

[Collection("Page spread shell")]
public sealed class VisualNoteSearchDialogTests(PageSpreadTestApplication application)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-TW")]
    public Task EveryResultCanBeLoadedAndOpenedWithoutRenderingLaterBatchesEarly(string language) => application.RunAsync(async () =>
    {
        var previousLanguage = L.Current.LanguageCode;
        L.SetLanguage(language);
        try
        {
            using var fixture = new SearchFixture(121);
            var original = JsonSerializer.Serialize(fixture.Document, DocumentJson.Options);
            await fixture.Dialog.SearchAsync();
            Assert.Equal(60, fixture.Results.Items.Count);
            Assert.Equal(60, fixture.Pdf.Requests.Count);
            Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 60, 121), fixture.Status.Text);
            Assert.True(fixture.More.IsEnabled);
            Assert.Equal(L.Text("Controls.VisualSearch.LoadMore"), AutomationProperties.GetName(fixture.More));
            Assert.True(fixture.More.MinHeight >= 44 && fixture.More.MinWidth >= 44);
            Assert.True(fixture.More.Focusable);
            fixture.Results.SelectedIndex = 2;
            var selection = fixture.Dialog.Result;

            await fixture.Dialog.LoadMoreAsync();
            Assert.Equal(120, fixture.Results.Items.Count);
            Assert.Equal(120, fixture.Pdf.Requests.Count);
            Assert.Same(selection, fixture.Dialog.Result);
            Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 120, 121), fixture.Status.Text);
            await fixture.Dialog.LoadMoreAsync();
            Assert.Equal(121, fixture.Results.Items.Count);
            Assert.Equal(fixture.Document.Pages.Select(page => page.Id), fixture.ResultIds);
            Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 121, 121), fixture.Status.Text);
            Assert.Equal(Visibility.Collapsed, fixture.More.Visibility);
            fixture.Results.SelectedIndex = 120;
            Assert.Equal(fixture.Document.Pages[120].Id, fixture.Dialog.Result!.PageId);
            Assert.True(fixture.Open.IsEnabled);
            await fixture.Dialog.LoadMoreAsync();
            Assert.Equal(121, fixture.Pdf.Requests.Count);
            Assert.Equal(original, JsonSerializer.Serialize(fixture.Document, DocumentJson.Options));
        }
        finally { L.SetLanguage(previousLanguage); }
    });

    [Fact]
    public Task ChangingFiltersCancelsThePendingBatchWithoutMixingOldRowsOrCounts() => application.RunAsync(async () =>
    {
        using var fixture = new SearchFixture(62);
        fixture.Document.Pages[61].Texts[0].Color = "#FF00AA00";
        // The dialog owns a snapshot, so construct the green match before opening it.
        fixture.RecreateDialog();
        await fixture.Dialog.SearchAsync();
        fixture.Pdf.HoldPageId = fixture.Document.Pages[60].Id;
        var pending = fixture.Dialog.LoadMoreAsync();
        await fixture.Pdf.Held.Task;
        Assert.False(fixture.More.IsEnabled);
        await fixture.Dialog.LoadMoreAsync();
        Assert.Equal(61, fixture.Pdf.Requests.Count);

        fixture.Color.SelectedItem = NoteColor.Green;
        await fixture.Dialog.SearchAsync();
        Assert.Equal(new[] { fixture.Document.Pages[61].Id }, fixture.ResultIds);
        Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 1, 1), fixture.Status.Text);
        fixture.Pdf.ReleaseHeld();
        await pending;
        Assert.Equal(new[] { fixture.Document.Pages[61].Id }, fixture.ResultIds);
        Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 1, 1), fixture.Status.Text);
        Assert.Equal(Visibility.Collapsed, fixture.More.Visibility);
    });

    [Fact]
    public Task FailedLaterPreviewKeepsLoadedResultsAndRetriesWithoutDuplicates() => application.RunAsync(async () =>
    {
        using var fixture = new SearchFixture(63);
        await fixture.Dialog.SearchAsync();
        fixture.Results.SelectedIndex = 5;
        var selection = fixture.Dialog.Result;
        fixture.Pdf.FailPageId = fixture.Document.Pages[61].Id;
        await fixture.Dialog.LoadMoreAsync();
        Assert.Equal(61, fixture.Results.Items.Count);
        Assert.Same(selection, fixture.Dialog.Result);
        Assert.True(fixture.More.IsEnabled);
        Assert.Contains(L.Text("Controls.VisualSearch.ShowingCount", 61, 63), fixture.Status.Text);
        Assert.Contains("Synthetic preview failure", fixture.Status.Text);

        fixture.Pdf.FailPageId = null;
        await fixture.Dialog.LoadMoreAsync();
        Assert.Equal(fixture.Document.Pages.Select(page => page.Id), fixture.ResultIds);
        Assert.Equal(2, fixture.Pdf.Requests.Count(id => id == fixture.Document.Pages[61].Id));
        Assert.Equal(L.Text("Controls.VisualSearch.ShowingCount", 63, 63), fixture.Status.Text);
        Assert.Equal(Visibility.Collapsed, fixture.More.Visibility);
    });

    [Fact]
    public Task ClosingDuringPreviewLoadingDoesNotAppendAnyMoreRows() => application.RunAsync(async () =>
    {
        using var fixture = new SearchFixture(61);
        await fixture.Dialog.SearchAsync();
        fixture.Pdf.HoldPageId = fixture.Document.Pages[60].Id;
        var pending = fixture.Dialog.LoadMoreAsync();
        await fixture.Pdf.Held.Task;
        fixture.Dialog.Close();
        fixture.Pdf.ReleaseHeld();
        await pending;
        Assert.Equal(60, fixture.Results.Items.Count);
    });

    private sealed class SearchFixture : IDisposable
    {
        public NotebookDocument Document { get; }
        public CountingPdf Pdf { get; } = new();
        public VisualNoteSearchDialog Dialog { get; private set; }
        public ListBox Results => Field<ListBox>("_results");
        public TextBlock Status => Field<TextBlock>("_status");
        public Button More => Field<Button>("_loadMore");
        public ComboBox Color => Field<ComboBox>("_color");
        public Button Open => Logical<Button>((DependencyObject)Dialog.Content).Single(button =>
            AutomationProperties.GetName(button) == L.Text("Controls.VisualSearch.Open"));
        public string[] ResultIds => Results.Items.Cast<ListBoxItem>().Select(item => ((VisualNoteMatch)item.Tag).PageId).ToArray();

        public SearchFixture(int count)
        {
            var section = new NoteSection { Id = "synthetic-section", Title = "Synthetic" };
            Document = new NotebookDocument { Sections = [section], Pages = Enumerable.Range(1, count).Select(index => new NotePage
            {
                Id = "page-" + index, SectionId = section.Id, Title = "Result " + index, Width = 300, Height = 100,
                Texts = [new() { X = 10, Y = 10, Width = 250, Height = 60, Text = "Matching note " + index, Color = "#FFCC3333" }]
            }).ToList() };
            Dialog = CreateDialog();
        }

        public void RecreateDialog() { Dialog.Close(); Dialog = CreateDialog(); }
        private VisualNoteSearchDialog CreateDialog() => new(null!, Document, new PagePreviewRenderer(Pdf, _ => throw new NotSupportedException()));
        private T Field<T>(string name) => (T)typeof(VisualNoteSearchDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Dialog)!;
        public void Dispose() { Dialog.Close(); Pdf.ReleaseHeld(); }
    }

    private sealed class CountingPdf : IPdfService
    {
        private readonly BitmapSource _bitmap;
        private readonly TaskCompletionSource<BitmapSource> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Requests { get; } = [];
        public string? HoldPageId { get; set; }
        public string? FailPageId { get; set; }
        public CountingPdf()
        {
            _bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);
            _bitmap.Freeze();
        }
        public Task<BitmapSource> RenderAsync(NotePage page, double scale, CancellationToken cancellationToken = default)
        {
            Requests.Add(page.Id);
            if (page.Id == FailPageId) throw new IOException("Synthetic preview failure");
            if (page.Id == HoldPageId) { Held.TrySetResult(); return _release.Task; }
            cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(_bitmap);
        }
        public void ReleaseHeld() => _release.TrySetResult(_bitmap);
        public Task<IReadOnlyList<NotePage>> ImportAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string path, NotebookDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { if (child is T item) yield return item; foreach (var nested in Logical<T>(child)) yield return nested; }
    }
}
