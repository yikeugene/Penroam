using System.IO;
using System.Security.Cryptography;
using Moye.Models;
using Moye.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Moye.Tests;

public sealed class DocumentWorkflowTests
{
    [Fact]
    public void RangeAcceptsUnicodeDashAndDuplicatesButUsesDocumentOrder()
        => Assert.Equal(new[] { 0, 1, 2, 4, 7 }, DocumentWorkflows.ParsePageRange("8, 1–3, 2, 5", 10));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("5-2")]
    [InlineData("1-")]
    [InlineData("1,,2")]
    [InlineData("1-2-3")]
    [InlineData("2147483648")]
    public void InvalidRangesNeverProducePartialSelections(string value)
        => Assert.Throws<FormatException>(() => DocumentWorkflows.ParsePageRange(value, 10));

    [Fact]
    public void ExportSnapshotRespectsDisplayedOrderAndFullyIsolatesMutableContent()
    {
        var section = new NoteSection();
        var document = new NotebookDocument { Sections = [section], Pages = [Page(section.Id, "one"), Page(section.Id, "two"), Page(section.Id, "three")] };
        document.Pages[0].InkData = [1, 2];
        var snapshot = DocumentWorkflows.ExportSnapshot(document, [document.Pages[2].Id, document.Pages[0].Id], "Selected");
        Assert.Equal(new[] { "one", "three" }, snapshot.Pages.Select(page => page.Texts[0].Text));
        snapshot.Pages[0].Texts[0].Text = "changed"; snapshot.Pages[0].InkData[0] = 99; snapshot.Sections[0].Title = "changed";
        Assert.Equal("one", document.Pages[0].Texts[0].Text); Assert.Equal(1, document.Pages[0].InkData[0]); Assert.Equal("General", section.Title);
        Assert.Equal(3, document.Pages.Count); Assert.Equal("Selected", snapshot.Title);
    }

    [Fact]
    public void ImportIntoEmptyMiddleSectionStaysBeforeFollowingSectionAndKeepsSourceUntouched()
    {
        var first = new NoteSection(); var middle = new NoteSection(); var last = new NoteSection();
        var document = new NotebookDocument { Sections = [first, middle, last], Pages = [Page(first.Id, "first"), Page(last.Id, "last")] };
        var source = Page("source", "imported"); source.Pdf = new() { AssetId = "original", PageIndex = 8 };
        var inserted = DocumentWorkflows.InsertPages(document, [source], middle.Id);
        Assert.Equal(new[] { "first", "imported", "last" }, document.Pages.Select(page => page.Texts[0].Text));
        Assert.Equal(middle.Id, inserted[0].SectionId); Assert.Equal("source", source.SectionId);
        Assert.NotEqual(source.Id, inserted[0].Id); Assert.NotEqual(source.Texts[0].Id, inserted[0].Texts[0].Id);
        Assert.Equal("original", inserted[0].Pdf!.AssetId); Assert.Equal(8, inserted[0].Pdf!.PageIndex);
    }

    [Fact]
    public void ImportAfterCurrentDoesNotChangeOtherSectionOrder()
    {
        var section = new NoteSection(); var other = new NoteSection();
        var document = new NotebookDocument { Sections = [section, other], Pages = [Page(section.Id, "one"), Page(section.Id, "two"), Page(other.Id, "other")] };
        DocumentWorkflows.InsertPages(document, [Page("", "new")], section.Id, document.Pages[0].Id);
        Assert.Equal(new[] { "one", "new", "two", "other" }, document.Pages.Select(page => page.Texts[0].Text));
        Assert.Throws<ArgumentException>(() => DocumentWorkflows.InsertPages(document, [Page("", "bad")], "missing"));
        Assert.Equal(4, document.Pages.Count);
    }

    [Fact]
    public async Task PreparedImportPreservesOriginalAndWritesLibraryAssetsOnlyWhenAccepted()
    {
        using var directory = new StorageTestDirectory();
        var bytes = WorkflowRepository.CreatePdf();
        var path = Path.Combine(directory.Root, "source.pdf"); await File.WriteAllBytesAsync(path, bytes);
        using var repository = new WorkflowRepository();
        using var prepared = new PreparedDocumentImport();
        var progress = new List<DocumentProgress>();
        await prepared.PrepareAsync(path, new InlineProgress(progress.Add), CancellationToken.None);
        Assert.Equal(3, prepared.Pages.Count); Assert.Equal(0, repository.Writes);
        Assert.Contains(progress, update => update.Total == 3 && update.Completed == 3);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.CommitAssetsAsync(repository, [prepared.Pages[1]], null, cancellation.Token));
        Assert.Equal(0, repository.Writes);
        await prepared.CommitAssetsAsync(repository, [prepared.Pages[1]], null, CancellationToken.None);
        Assert.Equal(1, repository.Writes);
        var stored = await repository.GetAssetAsync(prepared.Pages[1].Pdf!.AssetId);
        Assert.Equal(bytes, stored.Bytes); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, prepared.Pages[1].Pdf!.PageIndex);
    }

    [Fact]
    public async Task CancellationDuringExportLeavesExistingDestinationUntouched()
    {
        using var directory = new StorageTestDirectory(); using var repository = new WorkflowRepository();
        var destination = Path.Combine(directory.Root, "existing.pdf"); var original = new byte[] { 4, 5, 6 }; await File.WriteAllBytesAsync(destination, original);
        using var cancellation = new CancellationTokenSource();
        var document = new NotebookDocument { Pages = [new NotePage(), new NotePage()] };
        var progress = new InlineProgress(update => { if (update.Total == 2 && update.Completed == 1) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PdfService(repository).ExportAsync(destination, document, progress, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Single(Directory.GetFiles(directory.Root));
    }

    private static NotePage Page(string section, string text) => new() { SectionId = section, Texts = [new() { Text = text }] };
    private sealed class InlineProgress(Action<DocumentProgress> report) : IProgress<DocumentProgress> { public void Report(DocumentProgress value) => report(value); }
}

internal sealed class WorkflowRepository : INotebookRepository
{
    private readonly Dictionary<string, AssetData> _assets = [];
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
    public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
    public Task SaveAsync(NotebookDocument document) => Task.CompletedTask;
    public Task DeleteAsync(string id) => Task.CompletedTask;
    public Task<AssetData> PutAssetAsync(string name, string type, byte[] bytes)
    {
        var asset = new AssetData(Convert.ToHexStringLower(SHA256.HashData(bytes)), name, type, bytes.ToArray());
        _assets[asset.Id] = asset; Writes++; return Task.FromResult(asset);
    }
    public Task<AssetData> GetAssetAsync(string id) { Reads++; return Task.FromResult(_assets[id]); }
    public void Dispose() { }
    public static byte[] CreatePdf()
    {
        var builder = new PdfDocumentBuilder(); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("Calculus alpha", 18, new PdfPoint(40, 720), font);
        builder.AddPage(PageSize.A4).AddText("Algebra beta", 18, new PdfPoint(40, 720), font);
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }
}
