using Moye.Localization;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class AttachmentLimitsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(536870911)]
    [InlineData(536870912)]
    public void EditableBackupAttachmentBoundaryRemainsInclusive(long length)
        => AttachmentLimits.ValidateLength(length);

    [Theory]
    [InlineData(536870913)]
    [InlineData(long.MaxValue)]
    public void LargerAttachmentsReportTheEditableBackupLimit(long length)
    {
        var exception = Assert.Throws<InvalidDataException>(() => AttachmentLimits.ValidateLength(length));
        Assert.Equal(LocalizationService.Text("Messages.Import.AttachmentTooLarge"), exception.Message);
    }

    [Fact]
    public async Task PdfAndConvertedPdfAreRejectedBeforeParsingOrAssetStorage()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "oversized.pdf");
        // Set only the file length: the test never allocates a 512 MiB managed buffer.
        using (var file = File.Create(path)) file.SetLength((long)AttachmentLimits.MaximumBytes + 1);
        using var repository = new RejectAssetRepository();

        var importError = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfService(repository).ImportAsync(path));
        var preparationError = await Assert.ThrowsAsync<InvalidDataException>(() => OfficePdfPreparation.PrepareAsync(path));

        Assert.Equal(LocalizationService.Text("Messages.Import.AttachmentTooLarge"), importError.Message);
        Assert.Equal(importError.Message, preparationError.Message);
        Assert.Equal(0, repository.Writes);
        Assert.Equal((long)AttachmentLimits.MaximumBytes + 1, new FileInfo(path).Length);
        Assert.Single(Directory.GetFiles(directory.Root));
    }

    [Fact]
    public async Task BoundedReadPreservesOriginalBytesAndHonorsCancellation()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "original.bin");
        byte[] original = [1, 7, 2, 8, 255, 0];
        await File.WriteAllBytesAsync(path, original);
        Assert.Equal(original, await AttachmentLimits.ReadAllBytesAsync(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentLimits.ReadAllBytesAsync(path, new CancellationToken(true)));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private sealed class RejectAssetRepository : INotebookRepository
    {
        public int Writes { get; private set; }
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        { Writes++; throw new InvalidOperationException("An oversized PDF must not reach asset storage."); }
        public Task<AssetData> GetAssetAsync(string id) => throw new NotSupportedException();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => throw new NotSupportedException();
        public Task<NotebookDocument?> LoadAsync(string id) => throw new NotSupportedException();
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
