using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

/// <summary>Synthetic sources and fake conversion only; every staged asset repository is a private SQLite database.</summary>
public sealed class FolderImportTests
{
    [Fact]
    public async Task RealPdfBatchRoundTripsAsSectionsWithoutBlankPagesAndLeavesSourcesUntouched()
    {
        using var directory = new StorageTestDirectory();
        var folder = Directory.CreateDirectory(Path.Combine(directory.Root, "Course materials")).FullName;
        var pdf = WorkflowRepository.CreatePdf();
        var pdfPath = Path.Combine(folder, "2 Lecture.pdf"); await File.WriteAllBytesAsync(pdfPath, pdf);
        var slidesPath = Path.Combine(folder, "10 Slides.pptx"); var slides = Encoding.UTF8.GetBytes("Synthetic PowerPoint source");
        await File.WriteAllBytesAsync(slidesPath, slides);
        var converter = new TestConverter { Output = pdf };
        string? stagingPath = null;
        var service = new FolderImportService(converter, stagingRepositoryFactory: path =>
        {
            stagingPath = path; return new SqliteNotebookRepository(path);
        });
        using var library = new SqliteNotebookRepository(Path.Combine(directory.Root, "library.db"));
        var prepared = await service.PrepareAsync(folder);
        try
        {
            Assert.Equal("Course materials", prepared.Notebook.Title);
            Assert.Equal("My Notes", prepared.Notebook.Folder);
            Assert.Equal(new[] { "2 Lecture", "10 Slides" }, prepared.Notebook.Sections.Select(section => section.Title));
            Assert.Equal(6, prepared.Notebook.Pages.Count);
            Assert.Equal(2, prepared.Summary.ReadyCount);
            Assert.False(prepared.Summary.RequiresConfirmation);
            Assert.Empty(await library.ListAsync());
            Assert.True(File.Exists(stagingPath));
            await prepared.SaveAsync(library, () => Task.CompletedTask);
            Assert.True(prepared.HasSaved);
            var saved = Assert.Single(await library.ListAsync());
            var reopened = (await library.LoadAsync(saved.Id))!;
            Assert.Equal(prepared.Notebook.Id, saved.Id);
            Assert.Equal(2, reopened.Sections.Count); Assert.Equal(6, reopened.Pages.Count);
            Assert.DoesNotContain(reopened.Sections, section => section.Title == "General");
            Assert.All(reopened.Sections, section => Assert.Equal(3, reopened.Pages.Count(page => page.SectionId == section.Id)));
            Assert.Equal(new[] { 0, 1, 2, 0, 1, 2 }, reopened.Pages.Select(page => page.Pdf!.PageIndex));
            Assert.Equal(6, reopened.Pages.Select(page => page.Id).Distinct().Count());
            Assert.Equal(pdf, (await library.GetAssetAsync(reopened.Pages[0].Pdf!.AssetId)).Bytes);
            Assert.Equal(pdf, await File.ReadAllBytesAsync(pdfPath));
            Assert.Equal(slides, await File.ReadAllBytesAsync(slidesPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(converter.Inputs.Single())));
        }
        finally { prepared.Dispose(); }
        Assert.False(Directory.Exists(Path.GetDirectoryName(stagingPath)));
    }

    [Fact]
    public async Task DiscoveryIncludesOnlyTopLevelPdfAndPptxAndExplainsEverySkip()
    {
        using var fixture = new ImportFixture();
        fixture.Write("2 Notes.PDF"); fixture.Write("10 Slides.PPTX");
        foreach (var name in new[] { "old.ppt", "speaker.ppsx", "words.docx", "readme.txt", "photo.png" }) fixture.Write(name);
        var nested = Directory.CreateDirectory(Path.Combine(fixture.Folder, "nested.pdf")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nested, "invisible.pdf"), "1:nested");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.Equal(new[] { "2 Notes", "10 Slides" }, prepared.Notebook.Sections.Select(section => section.Title));
        Assert.Equal(2, prepared.Summary.ReadyCount); Assert.Equal(6, prepared.Summary.SkippedCount);
        Assert.Equal(0, prepared.Summary.FailedCount);
        Assert.Contains(prepared.Summary.Items, item => item.Name == "old.ppt" && item.Message.Contains("Save this presentation as .pptx"));
        Assert.Contains(prepared.Summary.Items, item => item.Name == "nested.pdf" && item.Message.Contains("Subfolder skipped"));
        Assert.DoesNotContain(prepared.Summary.Items, item => item.Name == "invisible.pdf");
        Assert.Single(fixture.Converter.Inputs);
        Assert.True(DocumentImportService.IsSupported("words.docx"));
        Assert.True(DocumentImportService.IsSupported("speaker.ppsx"));
        Assert.False(FolderImportService.IsSupported("words.docx"));
        Assert.False(FolderImportService.IsSupported("old.ppt"));
    }

    [Theory]
    [InlineData("missing.pdf", false)]
    [InlineData("unreadable.pdf", true)]
    [InlineData("temporary.txt", false)]
    public async Task DiscoveryAttributeFailuresKeepOtherDocumentsAvailableForAcceptance(string failedName, bool accessDenied)
    {
        using var fixture = new ImportFixture
        {
            ReadAttributes = path =>
            {
                if (Path.GetFileName(path) != failedName) return File.GetAttributes(path);
                if (accessDenied) throw new UnauthorizedAccessException("Synthetic access denied");
                throw new FileNotFoundException("Synthetic removed entry");
            }
        };
        fixture.Write("1 first.pdf"); fixture.Write(failedName); fixture.Write("2 last.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.Equal(2, prepared.Summary.ReadyCount);
        Assert.Equal(1, prepared.Summary.FailedCount);
        Assert.True(prepared.Summary.RequiresConfirmation);
        Assert.Contains(prepared.Summary.Items, item => item.Name == failedName && item.Status == FolderImportItemStatus.Failed && item.Message.Length > 0);
        Assert.Equal(new[] { "1 first", "2 last" }, prepared.Notebook.Sections.Select(section => section.Title));
        using var library = new MemoryRepository();
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SaveAsync(library, () => Task.CompletedTask));
        prepared.AcceptSuccessfulFiles();
        await prepared.SaveAsync(library, () => Task.CompletedTask);
        Assert.Single(library.Documents); Assert.Equal(2, library.Assets.Count);
    }

    [Fact]
    public async Task UnavailableSelectedFolderFailsBeforeCreatingStaging()
    {
        using var fixture = new ImportFixture();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => fixture.Service.PrepareAsync(Path.Combine(fixture.Folder, "missing folder")));
        Assert.Null(fixture.Staging);
    }

    [Fact]
    public async Task NaturalOrderHandlesHugeDigitRunsAndDisambiguatesStemsDeterministically()
    {
        using var fixture = new ImportFixture();
        var files = new[]
        {
            "Lesson 100000000000000000000000000000000000000.pdf", "Lesson 10.pdf", "Lesson 2.pptx",
            "Lesson 2.pdf", "Lesson 02.pdf", "Lesson 2 (2).pdf", "Lesson 99999999999999999999999999999999999999.pdf"
        };
        foreach (var file in files) fixture.Write(file);
        using var first = await fixture.Service.PrepareAsync(fixture.Folder);
        using var second = await fixture.Service.PrepareAsync(fixture.Folder);
        var names = first.Summary.Items.Select(item => item.Name).ToArray();
        Assert.Equal(new[]
        {
            "Lesson 2 (2).pdf", "Lesson 02.pdf", "Lesson 2.pdf", "Lesson 2.pptx", "Lesson 10.pdf",
            "Lesson 99999999999999999999999999999999999999.pdf", "Lesson 100000000000000000000000000000000000000.pdf"
        }, names);
        Assert.Equal(new[]
        {
            "Lesson 2 (2)", "Lesson 02", "Lesson 2", "Lesson 2 (3)", "Lesson 10",
            "Lesson 99999999999999999999999999999999999999", "Lesson 100000000000000000000000000000000000000"
        }, first.Notebook.Sections.Select(section => section.Title));
        Assert.Equal(first.Notebook.Sections.Select(section => section.Title), second.Notebook.Sections.Select(section => section.Title));
        Assert.Equal(files.Length, first.Notebook.Sections.Select(section => section.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.NotEqual(first.Notebook.Id, second.Notebook.Id);
        Assert.All(first.Notebook.Pages, page => Assert.DoesNotContain(page.Id, fixture.ImportedPageIds));
    }

    [Fact]
    public async Task PartialBatchRequiresAcceptanceAndCopiesOnlyAcceptedReferencedAssets()
    {
        using var fixture = new ImportFixture();
        fixture.Write("1 first.pdf", "2:first"); fixture.Write("2 damaged.pdf", "1:fail-after-asset"); fixture.Write("3 last.pdf", "3:last");
        using var library = new MemoryRepository();
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.Equal(2, prepared.Summary.ReadyCount); Assert.Equal(1, prepared.Summary.FailedCount);
        Assert.True(prepared.Summary.RequiresConfirmation);
        Assert.Equal(5, prepared.Notebook.Pages.Count);
        Assert.Equal(3, fixture.Staging!.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SaveAsync(library, () => Task.CompletedTask));
        Assert.Empty(library.Documents); Assert.Empty(library.Assets);
        prepared.AcceptSuccessfulFiles();
        await prepared.SaveAsync(library, () => Task.CompletedTask);
        Assert.Equal(2, library.Assets.Count); Assert.Single(library.Documents);
        Assert.Equal(new[] { "1 first", "3 last" }, library.Documents.Single().Value.Sections.Select(section => section.Title));
        Assert.DoesNotContain(library.Assets.Values, asset => Encoding.UTF8.GetString(asset.Bytes).Contains("fail-after-asset"));
        Assert.All(library.Documents.Single().Value.Sections, section => Assert.Contains(prepared.Notebook.Pages, page => page.SectionId == section.Id));
    }

    [Fact]
    public async Task RejectingPartialBatchDisposesStagingBeforeDeletingItsDirectory()
    {
        using var fixture = new ImportFixture();
        fixture.Write("1 okay.pdf"); fixture.Write("2 broken.pdf", "1:fail");
        using var library = new MemoryRepository();
        var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.True(prepared.Summary.RequiresConfirmation);
        prepared.Dispose(); prepared.Dispose();
        Assert.True(fixture.Staging!.Disposed);
        Assert.True(fixture.Staging.DirectoryExistedWhenDisposed);
        Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.Empty(library.Documents); Assert.Empty(library.Assets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoSuccessfulFilesNeverCreatesAnEmptyNotebook(bool hasBrokenFile)
    {
        using var fixture = new ImportFixture();
        fixture.Write("readme.txt");
        if (hasBrokenFile) fixture.Write("broken.pdf", "1:fail");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var library = new MemoryRepository();
        Assert.Equal(0, prepared.Summary.ReadyCount); Assert.False(prepared.Summary.RequiresConfirmation);
        Assert.Empty(prepared.Notebook.Sections); Assert.Empty(prepared.Notebook.Pages);
        prepared.AcceptSuccessfulFiles();
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SaveAsync(library, () => Task.CompletedTask));
        Assert.Empty(library.SaveAttempts); Assert.Empty(library.Assets);
    }

    [Fact]
    public async Task EmptyImportedDocumentIsAFailureNotAnEmptySection()
    {
        using var fixture = new ImportFixture(); fixture.Write("1 empty.pdf", "0:empty"); fixture.Write("2 notes.pdf", "2:notes");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.Equal(1, prepared.Summary.ReadyCount); Assert.Equal(1, prepared.Summary.FailedCount);
        Assert.Equal("2 notes", Assert.Single(prepared.Notebook.Sections).Title);
        Assert.Equal(2, prepared.Notebook.Pages.Count);
        Assert.Contains(prepared.Summary.Items, item => item.Name == "1 empty.pdf" && item.Message.Contains("no pages"));
    }

    [Fact]
    public async Task BatchProgressUsesFileCountsAndCancellationBeforeDiscoveryCreatesNoStaging()
    {
        using var fixture = new ImportFixture(); fixture.Write("1.pdf"); fixture.Write("2.pptx"); fixture.Write("skip.txt");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(fixture.Folder, cancellationToken: cancellation.Token));
        Assert.Null(fixture.Staging); Assert.Empty(fixture.Converter.Inputs);
        var updates = new List<DocumentProgress>();
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder, new InlineProgress(updates.Add));
        Assert.Contains(updates, update => update.Total == 2 && update.Completed == 0 && update.Message.Contains("1.pdf"));
        Assert.Contains(updates, update => update.Total == 2 && update.Completed == 1 && update.Message.Contains("2.pptx"));
        Assert.Equal(2, updates[^1].Completed); Assert.Equal(2, updates[^1].Total);
        Assert.All(updates, update => Assert.True(update.CanCancel));
    }

    [Fact]
    public async Task ConversionIsSequentialAndOnlyItsDisposableCopyCanBeModified()
    {
        using var fixture = new ImportFixture();
        var first = fixture.Write("1 slides.pptx", "original one");
        var second = fixture.Write("2 slides.pptx", "original two");
        var sourceBytes = new[] { await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second) };
        var active = 0; var maximumActive = 0;
        fixture.Converter.Convert = async (input, output, token) =>
        {
            maximumActive = Math.Max(maximumActive, ++active);
            Assert.NotEqual(fixture.Folder, Path.GetDirectoryName(input));
            await File.WriteAllTextAsync(input, "Converter may change only its staged copy", token);
            await Task.Yield();
            await File.WriteAllTextAsync(output, "1:" + Path.GetFileName(input), token);
            active--;
        };
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        Assert.Equal(1, maximumActive); Assert.Equal(2, prepared.Summary.ReadyCount);
        Assert.Equal(sourceBytes[0], await File.ReadAllBytesAsync(first)); Assert.Equal(sourceBytes[1], await File.ReadAllBytesAsync(second));
        Assert.Equal(2, fixture.Converter.Inputs.Select(Path.GetDirectoryName).Distinct().Count());
        Assert.All(fixture.Converter.Inputs, input => Assert.False(Directory.Exists(Path.GetDirectoryName(input))));
    }

    [Fact]
    public async Task CancellationAfterEarlierSuccessPropagatesAndCleansAllStaging()
    {
        using var fixture = new ImportFixture();
        fixture.Write("1 valid.pdf"); var slides = fixture.Write("2 slides.pptx", "untouched source"); fixture.Write("3 never.pdf");
        using var cancellation = new CancellationTokenSource();
        fixture.Converter.Convert = (_, _, token) =>
        {
            cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(fixture.Folder, cancellationToken: cancellation.Token));
        Assert.Single(fixture.ImportedPageIds);
        Assert.True(fixture.Staging!.Disposed); Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.Equal("untouched source", await File.ReadAllTextAsync(slides));
        Assert.All(fixture.Converter.Inputs, input => Assert.False(Directory.Exists(Path.GetDirectoryName(input))));
    }

    [Fact]
    public async Task ConverterCancellationWithoutCallerTokenIsNotReportedAsAFileFailure()
    {
        using var fixture = new ImportFixture(); fixture.Write("slides.pptx");
        fixture.Converter.Convert = (_, _, _) => throw new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(fixture.Folder));
        Assert.True(fixture.Staging!.Disposed); Assert.False(Directory.Exists(fixture.StagingDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CatastrophicStagingFailureAbortsInsteadOfOfferingAnIncompleteBatch(bool initialization)
    {
        using var fixture = new ImportFixture { FailInitialization = initialization, FailStagingWrite = initialization ? 0 : 2 };
        fixture.Write("1 first.pdf"); fixture.Write("2 second.pdf"); fixture.Write("3 never.pdf");
        var error = await Assert.ThrowsAsync<FolderImportStagingException>(() => fixture.Service.PrepareAsync(fixture.Folder));
        Assert.Contains("disk space", error.Message);
        Assert.Contains("Synthetic staging failure", error.InnerException!.Message);
        Assert.True(fixture.Staging!.Disposed); Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.Equal(initialization ? 0 : 1, fixture.ImportedPageIds.Count);
    }

    [Fact]
    public async Task OldAutosaveFailurePreventsAssetCopyAndKeepsPendingEdits()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var library = new MemoryRepository();
        using var autosave = new AutosaveCoordinator(library, TimeSpan.FromSeconds(2));
        var previous = new NotebookDocument { Title = "Existing unsaved notebook", Pages = [new() { Texts = [new() { Text = "Keep pending text" }] }] };
        autosave.Schedule(previous);
        library.BeforeSave = document => document.Id == previous.Id ? throw new IOException("Existing notebook cannot be saved") : Task.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => prepared.SaveAsync(library, autosave.FlushAsync));
        Assert.False(prepared.HasSaved); Assert.Empty(library.Assets); Assert.Empty(library.Documents);
        Assert.True(autosave.IsDirty);
        Assert.Equal("Keep pending text", Assert.Single(autosave.PendingDocuments).Pages[0].Texts[0].Text);
        Assert.DoesNotContain(prepared.Notebook.Id, library.SaveAttempts);
        library.BeforeSave = null;
        await prepared.SaveAsync(library, autosave.FlushAsync);
        Assert.False(autosave.IsDirty); Assert.Equal(2, library.Documents.Count);
    }

    [Fact]
    public async Task NoPublicationOrOpenIsAllowedUntilSaveCompletes()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var library = new MemoryRepository();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async _ => { entered.SetResult(); await release.Task; };
        var saving = prepared.SaveAsync(library, () => Task.CompletedTask);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(prepared.HasSaved); Assert.Empty(library.Documents);
            await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.OpenSavedAsync(_ => throw new Exception("Must not open")));
        }
        finally { release.SetResult(); }
        await saving;
        Assert.True(prepared.HasSaved); Assert.Single(library.Documents);
    }

    [Fact]
    public async Task FailedSaveRetriesSameNotebookIdWithoutConvertingAgain()
    {
        using var fixture = new ImportFixture(); fixture.Write("slides.pptx");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var library = new MemoryRepository { BeforeSave = _ => throw new IOException("Synthetic save failure") };
        var id = prepared.Notebook.Id;
        await Assert.ThrowsAsync<IOException>(() => prepared.SaveAsync(library, () => Task.CompletedTask));
        Assert.False(prepared.HasSaved); Assert.Empty(library.Documents);
        library.BeforeSave = null;
        await prepared.SaveAsync(library, () => Task.CompletedTask);
        Assert.True(prepared.HasSaved);
        Assert.Equal(new[] { id, id }, library.SaveAttempts); Assert.Single(library.Documents);
        Assert.Single(fixture.Converter.Inputs);
    }

    [Fact]
    public async Task RefreshFailureAfterSaveRetriesOpenOnlyAndRetainsSavedState()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var library = new MemoryRepository();
        await prepared.SaveAsync(library, () => Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => prepared.OpenSavedAsync(_ => throw new IOException("Library refresh failed")));
        Assert.True(prepared.HasSaved); Assert.Single(library.Documents);
        await prepared.SaveAsync(library, () => throw new Exception("A saved import must never save again"));
        string? opened = null;
        await prepared.OpenSavedAsync(id => { opened = id; return Task.CompletedTask; });
        Assert.Equal(prepared.Notebook.Id, opened); Assert.Single(library.SaveAttempts);
    }

    [Fact]
    public async Task CancellationDuringAssetCopyCanLeaveOnlyUnreferencedAssetsNeverAHalfNotebook()
    {
        using var fixture = new ImportFixture(); fixture.Write("1.pdf", "1:first"); fixture.Write("2.pdf", "1:second");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var cancellation = new CancellationTokenSource();
        using var library = new MemoryRepository { AfterPut = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.SaveAsync(library, () => Task.CompletedTask, cancellationToken: cancellation.Token));
        Assert.False(prepared.HasSaved); Assert.Single(library.Assets);
        Assert.Empty(library.SaveAttempts); Assert.Empty(library.Documents);
    }

    [Fact]
    public async Task CancellationAfterFinalBoundaryDoesNotMisreportCommittedSave()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var cancellation = new CancellationTokenSource(); using var library = new MemoryRepository();
        var progress = new InlineProgress(update => { if (!update.CanCancel) cancellation.Cancel(); });
        await prepared.SaveAsync(library, () => Task.CompletedTask, progress, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested); Assert.True(prepared.HasSaved); Assert.Single(library.Documents);
    }

    [Fact]
    public async Task AlreadyCanceledSaveDoesNotFlushOrWriteAnything()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var library = new MemoryRepository();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.SaveAsync(library,
            () => throw new Exception("Should not flush"), cancellationToken: cancellation.Token));
        Assert.Empty(library.Assets); Assert.Empty(library.SaveAttempts);
    }

    [Fact]
    public async Task TamperedStagedBytesAreRejectedBeforeAnyLibraryAssetIsWritten()
    {
        using var fixture = new ImportFixture(); fixture.Write("notes.pdf");
        using var prepared = await fixture.Service.PrepareAsync(fixture.Folder);
        fixture.Staging!.CorruptReads = true;
        using var library = new MemoryRepository();
        await Assert.ThrowsAsync<InvalidDataException>(() => prepared.SaveAsync(library, () => Task.CompletedTask));
        Assert.Empty(library.Assets); Assert.Empty(library.SaveAttempts); Assert.False(prepared.HasSaved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparedDocumentImportRespectsExplicitAssetRepositoryOwnership(bool ownsAssets)
    {
        var assets = new MemoryRepository();
        var prepared = new PreparedDocumentImport(assets, ownsAssets);
        prepared.Dispose(); prepared.Dispose();
        Assert.Equal(ownsAssets, assets.Disposed);
        assets.Dispose();
    }

    private sealed class ImportFixture : IDisposable
    {
        private readonly StorageTestDirectory _directory = new();
        public string Folder { get; }
        public string? StagingDirectory { get; private set; }
        public ObservedStagingRepository? Staging { get; private set; }
        public TestConverter Converter { get; } = new();
        public List<string> ImportedPageIds { get; } = [];
        public bool FailInitialization { get; init; }
        public int FailStagingWrite { get; init; }
        public Func<string, FileAttributes>? ReadAttributes { get; init; }
        public FolderImportService Service => new(Converter, repository => new SyntheticPdf(repository, ImportedPageIds), path =>
        {
            StagingDirectory = Path.GetDirectoryName(path);
            Staging = new ObservedStagingRepository(path) { FailInitialization = FailInitialization, FailWrite = FailStagingWrite };
            return Staging;
        }, ReadAttributes);
        public ImportFixture() => Folder = Directory.CreateDirectory(Path.Combine(_directory.Root, "Imported course")).FullName;
        public string Write(string name, string? value = null)
        {
            var path = Path.Combine(Folder, name); File.WriteAllText(path, value ?? "1:" + name); return path;
        }
        public void Dispose() => _directory.Dispose();
    }

    private sealed class TestConverter : IOfficePdfConverter
    {
        public byte[] Output { get; init; } = Encoding.UTF8.GetBytes("1:converted slide");
        public List<string> Inputs { get; } = [];
        public Func<string, string, CancellationToken, Task>? Convert { get; set; }
        public async Task ConvertAsync(string sourcePath, string outputPdfPath, CancellationToken cancellationToken = default)
        {
            Inputs.Add(sourcePath);
            if (Convert is not null) await Convert(sourcePath, outputPdfPath, cancellationToken);
            else await File.WriteAllBytesAsync(outputPdfPath, Output, cancellationToken);
        }
    }

    private sealed class SyntheticPdf(INotebookRepository assets, List<string> importedIds) : IPdfService
    {
        public async Task<IReadOnlyList<NotePage>> ImportAsync(string path, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var source = Encoding.UTF8.GetString(bytes);
            if (source.EndsWith(":fail", StringComparison.Ordinal)) throw new InvalidDataException("Synthetic damaged PDF");
            var asset = await assets.PutAssetAsync(Path.GetFileName(path), "application/pdf", bytes);
            if (source.Contains("fail-after-asset", StringComparison.Ordinal)) throw new InvalidDataException("Synthetic late PDF failure");
            var count = int.Parse(source.Split(':')[0]);
            var pages = Enumerable.Range(0, count).Select(index => new NotePage
            {
                Width = 600, Height = 800, Pdf = new() { AssetId = asset.Id, PageIndex = index }, Texts = [new() { Text = source }]
            }).ToArray();
            importedIds.AddRange(pages.Select(page => page.Id));
            return pages;
        }
        public Task<BitmapSource> RenderAsync(NotePage page, double scale, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string path, NotebookDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ObservedStagingRepository(string path) : INotebookRepository
    {
        private readonly SqliteNotebookRepository _inner = new(path);
        public int Writes { get; private set; }
        public int FailWrite { get; init; }
        public bool FailInitialization { get; init; }
        public bool CorruptReads { get; set; }
        public bool Disposed { get; private set; }
        public bool DirectoryExistedWhenDisposed { get; private set; }
        public Task InitializeAsync() => FailInitialization ? throw new IOException("Synthetic staging failure") : _inner.InitializeAsync();
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => _inner.ListAsync();
        public Task<NotebookDocument?> LoadAsync(string id) => _inner.LoadAsync(id);
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        {
            Writes++;
            return Writes == FailWrite ? throw new IOException("Synthetic staging failure") : _inner.PutAssetAsync(fileName, contentType, bytes);
        }
        public async Task<AssetData> GetAssetAsync(string id)
        {
            var asset = await _inner.GetAssetAsync(id);
            return CorruptReads ? asset with { Bytes = [0, 1, 2] } : asset;
        }
        public void Dispose()
        {
            DirectoryExistedWhenDisposed = Directory.Exists(Path.GetDirectoryName(path));
            _inner.Dispose(); Disposed = true;
        }
    }

    private sealed class MemoryRepository : INotebookRepository
    {
        public Dictionary<string, AssetData> Assets { get; } = [];
        public Dictionary<string, NotebookDocument> Documents { get; } = [];
        public List<string> SaveAttempts { get; } = [];
        public Func<NotebookDocument, Task>? BeforeSave { get; set; }
        public Action<AssetData>? AfterPut { get; init; }
        public bool Disposed { get; private set; }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>(
            Documents.Values.Select(document => new NotebookSummary { Id = document.Id, Title = document.Title }).ToArray());
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult(Documents.GetValueOrDefault(id)?.Snapshot());
        public async Task SaveAsync(NotebookDocument document)
        {
            SaveAttempts.Add(document.Id);
            if (BeforeSave is not null) await BeforeSave(document);
            Documents[document.Id] = document.Snapshot();
        }
        public Task DeleteAsync(string id) { Documents.Remove(id); return Task.CompletedTask; }
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        {
            var asset = new AssetData(Convert.ToHexStringLower(SHA256.HashData(bytes)), fileName, contentType, bytes.ToArray());
            Assets[asset.Id] = asset; AfterPut?.Invoke(asset); return Task.FromResult(asset);
        }
        public Task<AssetData> GetAssetAsync(string id) => Task.FromResult(Assets[id]);
        public void Dispose() => Disposed = true;
    }

    private sealed class InlineProgress(Action<DocumentProgress> report) : IProgress<DocumentProgress>
    {
        public void Report(DocumentProgress value) => report(value);
    }
}
