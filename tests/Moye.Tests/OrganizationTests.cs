using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class OrganizationTests
{
    [Fact]
    public async Task MetadataSurvivesStorageAndEditableBackupWithoutRestoredCopyTakingOverInbox()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var document = Sample(); document.IsPinned = true; document.CoverColor = "#536980"; document.IsQuickInbox = true;
        document.Pages[0].Title = "定義與證明"; document.Pages[0].IsBookmarked = true;
        await repository.SaveAsync(document);
        var saved = (await repository.LoadAsync(document.Id))!;
        Assert.True(saved.IsPinned); Assert.Equal("#536980", saved.CoverColor); Assert.True(saved.IsQuickInbox);
        Assert.Equal("定義與證明", saved.Pages[0].Title); Assert.True(saved.Pages[0].IsBookmarked);
        var summary = Assert.Single(await repository.ListAsync()); Assert.True(summary.IsPinned); Assert.Equal("#536980", summary.DisplayCoverColor);
        var backup = new BackupService(repository); var path = Path.Combine(directory.Root, "organization.moye");
        await backup.ExportAsync(path, [saved]);
        using (var archive = ZipFile.OpenRead(path))
        using (var stream = archive.GetEntry("manifest.json")!.Open()) Assert.Equal(3, JsonNode.Parse(stream)!["version"]!.GetValue<int>());
        var restored = Assert.Single(await backup.ImportAsync(path));
        Assert.True(restored.IsPinned); Assert.False(restored.IsQuickInbox); Assert.Equal(saved.CoverColor, restored.CoverColor);
        Assert.Equal(saved.Pages[0].Title, restored.Pages[0].Title); Assert.True(restored.Pages[0].IsBookmarked);
        Assert.NotEqual(saved.Pages[0].Id, restored.Pages[0].Id);
        var copy = saved.Snapshot(); copy.Pages[0].Title = "Changed"; copy.CoverColor = "#3B6656";
        Assert.Equal("定義與證明", saved.Pages[0].Title); Assert.Equal("#536980", saved.CoverColor);
    }

    [Fact]
    public async Task V2BackupDefaultsNewMetadataAndV3RequiresIt()
    {
        using var directory = new StorageTestDirectory(); using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var backup = new BackupService(repository); var path = Path.Combine(directory.Root, "older.moye");
        await backup.ExportAsync(path, [Sample()]);
        RewriteBackup(path, 2, root =>
        {
            root.Remove("isPinned"); root.Remove("coverColor"); root.Remove("isQuickInbox");
            foreach (var page in root["pages"]!.AsArray()) { page!.AsObject().Remove("title"); page.AsObject().Remove("isBookmarked"); }
        });
        var restored = Assert.Single(await backup.ImportAsync(path));
        Assert.False(restored.IsPinned); Assert.Equal("", restored.CoverColor); Assert.All(restored.Pages, page => { Assert.Equal("", page.Title); Assert.False(page.IsBookmarked); });
        RewriteBackup(path, 3, _ => { });
        await Assert.ThrowsAsync<InvalidDataException>(() => backup.ImportAsync(path));
    }

    [Fact]
    public async Task V2DatabaseMigrationRetainsPageBytesAndDefaultsMetadata()
    {
        using var directory = new StorageTestDirectory(); var original = Sample();
        using (var repository = new SqliteNotebookRepository(directory.DatabasePath)) await repository.SaveAsync(original);
        using (var connection = Open(directory.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "ALTER TABLE notebooks DROP COLUMN is_pinned; ALTER TABLE notebooks DROP COLUMN cover_color; ALTER TABLE notebooks DROP COLUMN is_quick_inbox; PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }
        using var upgraded = new SqliteNotebookRepository(directory.DatabasePath);
        var loaded = (await upgraded.LoadAsync(original.Id))!;
        Assert.Equal(original.Pages.Select(page => page.Id), loaded.Pages.Select(page => page.Id));
        Assert.Equal(original.Pages[0].Texts[0].Text, loaded.Pages[0].Texts[0].Text);
        Assert.False(loaded.IsPinned); Assert.Equal("", loaded.CoverColor);
        using var check = Open(directory.DatabasePath); using var query = check.CreateCommand(); query.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, query.ExecuteScalar());
    }

    [Fact]
    public async Task AtomicBatchRollsBackEveryNotebookWhenOneUpdateFails()
    {
        using var directory = new StorageTestDirectory(); using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var source = Sample(); var target = Sample(); await repository.SaveBatchAsync([source, target]);
        var altered = source.Snapshot(); altered.Title = "Must roll back";
        var invalid = target.Snapshot(); invalid.CoverColor = "invalid";
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveBatchAsync([altered, invalid]));
        Assert.Equal(source.Title, (await repository.LoadAsync(source.Id))!.Title);
        Assert.Equal(target.CoverColor, (await repository.LoadAsync(target.Id))!.CoverColor);
    }

    [Fact]
    public async Task SearchFindsFreshTypedTitlesSectionsAndBookmarksWithoutLoadingInk()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var original = Sample(); original.Pages[0].InkData = [1, 2, 3, 4]; await model.Repository.SaveAsync(original); await model.OpenAsync(original.Id);
        model.SetPageDetails(original.Pages[1].Id, "Exam theorem", true);
        model.Document!.Pages[0].Texts[0].Text = "A fresh unsaved 中文證明 with useful context";
        var matches = await model.SearchContentAsync("中文證明"); var hit = Assert.Single(matches);
        Assert.Equal(original.Pages[0].Id, hit.PageId); Assert.Contains("fresh unsaved", hit.Snippet);
        Assert.Contains("Lectures", hit.Location);
        Assert.Single(await model.SearchContentAsync("Exam theorem"));
        Assert.Single(await model.SearchContentAsync("Exercises"));
        var bookmark = Assert.Single(await model.SearchContentAsync("", true)); Assert.Equal(original.Pages[1].Id, bookmark.PageId);
        var projection = Assert.Single(await ((SqliteNotebookRepository)model.Repository).LoadSearchDocumentsAsync());
        Assert.All(projection.Pages, page => Assert.Empty(page.InkData));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, (await model.Repository.LoadAsync(original.Id))!.Pages[0].InkData);
    }

    [Fact]
    public void OrganizerDragAndMultiPageMovesPreserveOrderAndMixedContent()
    {
        var document = Sample(); var sourceIds = document.Pages.Select(page => page.Id).ToArray();
        var third = new NotePage { SectionId = document.Sections[0].Id, Title = "Third", Images = [new NoteImage { AssetId = "asset" }] };
        document.Pages.Insert(1, third);
        PageOrganization.MoveBefore(document, [sourceIds[1], third.Id], sourceIds[0]);
        Assert.Equal(new[] { third.Id, sourceIds[1], sourceIds[0] }, document.Pages.Select(page => page.Id));
        Assert.All(document.Pages, page => Assert.Equal(document.Sections[0].Id, page.SectionId));
        Assert.Equal("asset", document.Pages[0].Images[0].AssetId);
        PageOrganization.MoveBy(document, [third.Id, sourceIds[1]], 1);
        Assert.Equal(new[] { sourceIds[0], third.Id, sourceIds[1] }, document.Pages.Select(page => page.Id));
        PageOrganization.MoveToSection(document, [third.Id, sourceIds[0]], document.Sections[1].Id);
        Assert.Equal(new[] { sourceIds[1], sourceIds[0], third.Id }, document.Pages.Select(page => page.Id));
    }

    [Fact]
    public async Task PdfSearchFindsOriginalPageWithoutAddingTextBoxesToCurrentOrStoredNotes()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var asset = await model.Repository.PutAssetAsync("reference.pdf", "application/pdf", WorkflowRepository.CreatePdf());
        var document = Sample(); document.Pages[0].Pdf = new() { AssetId = asset.Id, PageIndex = 1 };
        document.Pages[1].Pdf = new() { AssetId = asset.Id, PageIndex = 2 };
        await model.Repository.SaveAsync(document); await model.OpenAsync(document.Id);
        var currentBefore = JsonSerializer.Serialize(model.Document, DocumentJson.Options);
        var result = Assert.Single(await model.SearchContentAsync("Algebra beta"));
        Assert.Equal(document.Pages[0].Id, result.PageId); Assert.Contains("Algebra beta", result.Snippet);
        Assert.Contains("1 page(s)", model.SearchCoverageMessage); Assert.Contains("OCR", model.SearchCoverageMessage);
        Assert.Equal(currentBefore, JsonSerializer.Serialize(model.Document, DocumentJson.Options));
        Assert.Equal(currentBefore, JsonSerializer.Serialize(await model.Repository.LoadAsync(document.Id), DocumentJson.Options));
        Assert.False(model.Autosave.IsDirty);
    }

    [Fact]
    public async Task ApplyingManyOrganizerChangesIsOneUndoStepAndCancelSnapshotIsIsolated()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var original = Sample(); model.ReplaceDocument(original, true);
        var edited = original.Snapshot(); var copies = PageOrganization.Duplicate(edited, [edited.Pages[0].Id]);
        edited.Pages[0].Title = "Changed title"; PageOrganization.MoveToSection(edited, copies, edited.Sections[1].Id);
        Assert.Equal("", original.Pages[0].Title); Assert.Equal(2, original.Pages.Count);
        model.ApplyOrganization(edited); Assert.True(model.CanUndo); Assert.Equal(3, model.Document!.Pages.Count);
        model.Undo(); Assert.False(model.CanUndo); Assert.Equal(2, model.Document!.Pages.Count); Assert.Equal("", model.Document.Pages[0].Title);
        model.Redo(); Assert.Equal(3, model.Document!.Pages.Count); Assert.Equal("Changed title", model.Document.Pages[0].Title);
        await model.Autosave.FlushAsync();
        Assert.Equal(3, (await model.Repository.LoadAsync(original.Id))!.Pages.Count);
    }

    [Fact]
    public async Task CrossNotebookTransferAndExplicitUndoAreAtomicAndKeepSharedAttachments()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var asset = await model.Repository.PutAssetAsync("image.png", "image/png", [1, 3, 5]);
        var source = Sample(); source.Pages[0].Images.Add(new NoteImage { AssetId = asset.Id }); source.Pages[0].Title = "Move me";
        var target = Sample(); await model.Repository.SaveAsync(source); await model.Repository.SaveAsync(target); await model.OpenAsync(source.Id);
        var receipt = await model.TransferPagesAsync([source.Pages[0].Id], target.Id, target.Sections[1].Id, false);
        Assert.Single((await model.Repository.LoadAsync(source.Id))!.Pages);
        var moved = (await model.Repository.LoadAsync(target.Id))!.Pages.Single(page => page.Id == source.Pages[0].Id);
        Assert.Equal(target.Sections[1].Id, moved.SectionId); Assert.Equal("Move me", moved.Title); Assert.Equal(asset.Id, moved.Images[0].AssetId);
        Assert.Equal(asset.Bytes, (await model.Repository.GetAssetAsync(asset.Id)).Bytes);
        await model.UndoTransferAsync(receipt);
        Assert.Equal(source.Pages.Select(page => page.Id), (await model.Repository.LoadAsync(source.Id))!.Pages.Select(page => page.Id));
        Assert.Equal(target.Pages.Select(page => page.Id), (await model.Repository.LoadAsync(target.Id))!.Pages.Select(page => page.Id));
    }

    [Fact]
    public async Task TransferRejectsMissingAttachmentsAndUndoRefusesToOverwriteLaterEdits()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var source = Sample(); var target = Sample(); source.Pages[0].Images.Add(new NoteImage { AssetId = "missing" });
        await model.Repository.SaveAsync(source); await model.Repository.SaveAsync(target); await model.OpenAsync(source.Id);
        await Assert.ThrowsAsync<FileNotFoundException>(() => model.TransferPagesAsync([source.Pages[0].Id], target.Id, target.Sections[0].Id, false));
        Assert.Equal(2, (await model.Repository.LoadAsync(source.Id))!.Pages.Count); Assert.Equal(2, (await model.Repository.LoadAsync(target.Id))!.Pages.Count);
        var receipt = await model.TransferPagesAsync([source.Pages[1].Id], target.Id, target.Sections[0].Id, true);
        var later = (await model.Repository.LoadAsync(target.Id))!; later.Title = "Later edit"; await model.Repository.SaveAsync(later);
        await Assert.ThrowsAsync<InvalidOperationException>(() => model.UndoTransferAsync(receipt));
        Assert.Equal("Later edit", (await model.Repository.LoadAsync(target.Id))!.Title);
    }

    [Fact]
    public async Task QuickNotesReuseInboxAndSelectTheNewDatedPage()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        await model.CreateQuickNoteAsync(); var id = model.Document!.Id; var first = model.SelectedPage!.Page.Id;
        Assert.True(model.Document.IsQuickInbox); Assert.True(model.Document.IsPinned); Assert.False(model.IsLibraryVisible);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", model.SelectedPage.Page.Title);
        await model.CreateQuickNoteAsync(); Assert.Equal(id, model.Document!.Id); Assert.Equal(2, model.Document.Pages.Count);
        Assert.NotEqual(first, model.SelectedPage!.Page.Id); Assert.Single(await model.Repository.ListAsync());
    }

    [Fact]
    public void TextContinuationIsOneUndoStepAndCreatesAPaperPage()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var original = Sample(); original.Pages[0].Pdf = new PdfPageSource { AssetId = "pdf" }; original.Pages[0].Width = 600;
        var source = original.Pages[0]; source.Texts[0].Text = "First\nSecond"; model.ReplaceDocument(original, true);
        var continuation = source.Texts[0] with { Text = "Second", Bold = true, X = 40, Y = 40 };
        Assert.True(model.ContinueTextOnNewPage(source.Id, source.Texts[0].Id, "First\n", continuation));
        var page = model.SelectedPage!.Page; Assert.Null(page.Pdf); Assert.Equal(600, page.Width); Assert.Equal(source.SectionId, page.SectionId);
        Assert.Equal("Second", Assert.Single(page.Texts).Text); Assert.True(page.Texts[0].Bold); Assert.NotEqual(continuation.Id, page.Texts[0].Id);
        model.Undo(); Assert.False(model.CanUndo); Assert.Equal(2, model.Document!.Pages.Count); Assert.Equal("First\nSecond", model.Document.Pages[0].Texts[0].Text);
    }

    [Fact]
    public async Task PinnedSortingAndCategoryFilterDoNotChangeStableCoverIdentity()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var first = Sample(); first.Title = "Zebra"; first.Folder = "Math"; first.IsPinned = true;
        var second = Sample(); second.Title = "Alpha"; second.Folder = "Physics";
        await model.Repository.SaveAsync(first); await model.Repository.SaveAsync(second); await model.RefreshLibraryAsync();
        var color = model.Notebooks.Single(note => note.Id == first.Id).DisplayCoverColor;
        model.SortNotebooksByName = true; Assert.Equal(first.Id, model.Notebooks[0].Id);
        model.Search = "Zebra"; Assert.Equal(color, Assert.Single(model.Notebooks).DisplayCoverColor);
        model.Search = ""; model.CategoryFilter = "Physics"; Assert.Equal(second.Id, Assert.Single(model.Notebooks).Id);
    }

    private static NotebookDocument Sample()
    {
        var lectures = new NoteSection { Title = "Lectures" }; var exercises = new NoteSection { Title = "Exercises" };
        return new NotebookDocument { Title = "Course", Folder = "Study", Sections = [lectures, exercises],
            Pages = [new NotePage { SectionId = lectures.Id, Texts = [new NoteText { Text = "The first lecture" }] },
                new NotePage { SectionId = exercises.Id, Texts = [new NoteText { Text = "Practice questions" }] }] };
    }
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open(); return connection;
    }
    private static void RewriteBackup(string path, int version, Action<JsonObject> edit)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        JsonNode Read(string name) { using var stream = archive.GetEntry(name)!.Open(); return JsonNode.Parse(stream)!; }
        void Write(string name, byte[] bytes) { archive.GetEntry(name)!.Delete(); using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes); }
        var document = Read("notebooks/000000.json").AsObject(); edit(document); var bytes = Encoding.UTF8.GetBytes(document.ToJsonString()); Write("notebooks/000000.json", bytes);
        var manifest = Read("manifest.json"); manifest["version"] = version; manifest["notebooks"]![0]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Write("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }
}
