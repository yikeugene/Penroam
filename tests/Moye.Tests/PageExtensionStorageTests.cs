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

public sealed class PageExtensionStorageTests
{
    [Fact]
    public async Task ExpandedPdfPlacementSurvivesSnapshotHistorySqliteAndEditableBackup()
    {
        using var sourceDirectory = new StorageTestDirectory();
        using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("original.pdf", "application/pdf", [1, 3, 7, 9]);
        var document = Sample(asset.Id);
        var original = document.Snapshot();
        var history = new NotebookHistory(); history.Reset(document);
        document.Pages[0].Width += 150;
        document.Pages[0].Pdf!.OffsetX += 150;
        history.Record(document);
        Assert.Equal(original.Pages[0].Pdf, history.Undo()!.Pages[0].Pdf);
        Assert.Equal(document.Pages[0].Pdf, history.Redo()!.Pages[0].Pdf);
        Assert.NotSame(original.Pages[0].Pdf, document.Pages[0].Pdf);

        await source.SaveAsync(document);
        using var reopened = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        var saved = (await reopened.LoadAsync(document.Id))!;
        AssertPlacement(document.Pages[0], saved.Pages[0]);
        AssertPlacement(document.Pages[0], Assert.Single(await reopened.LoadSearchDocumentsAsync()).Pages[0]);
        var backupPath = Path.Combine(sourceDirectory.Root, "expanded.moye");
        await new BackupService(reopened).ExportAsync(backupPath, [saved]);
        using (var archive = ZipFile.OpenRead(backupPath))
            Assert.Equal(4, ReadJson(archive.GetEntry("manifest.json")!)["version"]!.GetValue<int>());
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var restored = Assert.Single(await new BackupService(destination).ImportAsync(backupPath));
        AssertPlacement(saved.Pages[0], restored.Pages[0]);
        Assert.NotEqual(saved.Pages[0].Id, restored.Pages[0].Id);
        Assert.Equal(asset.Bytes, (await destination.GetAssetAsync(asset.Id)).Bytes);
        await destination.SaveAsync(restored);
        AssertPlacement(saved.Pages[0], (await destination.LoadAsync(restored.Id))!.Pages[0]);
    }

    [Fact]
    public async Task V3DatabaseMigrationRetainsLegacyJsonInkAndHashesAndUsesZeroPlacementDefaults()
    {
        using var directory = new StorageTestDirectory();
        var document = Sample(new string('a', 64));
        document.Pages[0].Pdf = new PdfPageSource { AssetId = new string('a', 64), CropWidth = 600, CropHeight = 800 };
        document.Pages[0].InkData = [1, 4, 9];
        using (var repository = new SqliteNotebookRepository(directory.DatabasePath)) await repository.SaveAsync(document);
        string legacyJson;
        using (var connection = Open(directory.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            var page = JsonSerializer.SerializeToNode(document.Pages[0], DocumentJson.Options)!;
            RemovePlacement(page["pdf"]!.AsObject());
            page.AsObject().Remove("paperLayout");
            legacyJson = page.ToJsonString();
            command.CommandText = "UPDATE pages SET metadata_json=$json,content_hash='retain-hash'; PRAGMA user_version=3;";
            command.Parameters.AddWithValue("$json", legacyJson);
            command.ExecuteNonQuery();
        }
        using var upgraded = new SqliteNotebookRepository(directory.DatabasePath);
        var loaded = (await upgraded.LoadAsync(document.Id))!;
        Assert.Equal(document.Pages[0].InkData, loaded.Pages[0].InkData);
        Assert.Equal(0, loaded.Pages[0].Pdf!.OffsetX);
        Assert.Equal(0, loaded.Pages[0].Pdf!.OffsetY);
        Assert.Equal(0, loaded.Pages[0].Pdf!.DisplayWidth);
        Assert.Equal(0, loaded.Pages[0].Pdf!.DisplayHeight);
        Assert.Null(loaded.Pages[0].PaperLayout);
        Assert.True(PdfPagePlacement.IsValid(loaded.Pages[0]));
        using var check = Open(directory.DatabasePath); using var query = check.CreateCommand();
        query.CommandText = "PRAGMA user_version"; Assert.Equal(4L, query.ExecuteScalar());
        query.CommandText = "SELECT metadata_json FROM pages"; Assert.Equal(legacyJson, query.ExecuteScalar());
        query.CommandText = "SELECT content_hash FROM pages"; Assert.Equal("retain-hash", query.ExecuteScalar());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LegacyBackupVersionsWithoutPlacementRetainOriginalPdfLayout(int version)
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var asset = await repository.PutAssetAsync("original.pdf", "application/pdf", [3, 1, 4]);
        var document = Sample(asset.Id);
        document.Pages[0].Pdf = new PdfPageSource { AssetId = asset.Id, CropWidth = 600, CropHeight = 800 };
        var path = Path.Combine(directory.Root, "legacy.moye");
        var backups = new BackupService(repository);
        await backups.ExportAsync(path, [document]);
        RewriteBackup(path, root =>
        {
            RemovePlacement(root["pages"]![0]!["pdf"]!.AsObject());
            root["pages"]![0]!.AsObject().Remove("paperLayout");
        }, version);
        var restored = Assert.Single(await backups.ImportAsync(path));
        AssertPlacement(document.Pages[0], restored.Pages[0]);
        Assert.True(PdfPagePlacement.IsValid(restored.Pages[0]));
        Assert.Null(restored.Pages[0].PaperLayout);
    }

    [Theory]
    [InlineData("offsetX")]
    [InlineData("offsetY")]
    [InlineData("displayWidth")]
    [InlineData("displayHeight")]
    [InlineData("outside-canvas")]
    [InlineData("partial-size")]
    public async Task V4BackupRejectsIncompleteOrInvalidPlacementBeforeImportingAssets(string corruption)
    {
        using var sourceDirectory = new StorageTestDirectory();
        using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("original.pdf", "application/pdf", [9, 2, 6]);
        var path = Path.Combine(sourceDirectory.Root, "damaged.moye");
        await new BackupService(source).ExportAsync(path, [Sample(asset.Id)]);
        RewriteBackup(path, root =>
        {
            var pdf = root["pages"]![0]!["pdf"]!.AsObject();
            if (corruption == "outside-canvas") pdf["offsetX"] = 9999;
            else if (corruption == "partial-size") pdf["displayWidth"] = 0;
            else pdf.Remove(corruption);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(destination).ImportAsync(path));
        Assert.Empty(await destination.ListAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => destination.GetAssetAsync(asset.Id));
    }

    [Theory]
    [InlineData("negative-x")]
    [InlineData("negative-y")]
    [InlineData("nan-offset")]
    [InlineData("infinite-size")]
    [InlineData("partial-size")]
    [InlineData("negative-size")]
    [InlineData("legacy-offset")]
    [InlineData("outside-x")]
    [InlineData("outside-y")]
    public async Task InvalidPlacementCannotOverwriteSavedNotebookOrBackup(string invalid)
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var asset = await repository.PutAssetAsync("original.pdf", "application/pdf", [5, 3, 5]);
        var original = Sample(asset.Id);
        await repository.SaveAsync(original);
        var path = Path.Combine(directory.Root, "valid.moye");
        var backups = new BackupService(repository);
        await backups.ExportAsync(path, [original]);
        var backupBytes = await File.ReadAllBytesAsync(path);
        var changed = original.Snapshot(); changed.Title = "Must roll back";
        var pdf = changed.Pages[0].Pdf!;
        switch (invalid)
        {
            case "negative-x": pdf.OffsetX = -1; break;
            case "negative-y": pdf.OffsetY = -1; break;
            case "nan-offset": pdf.OffsetY = double.NaN; break;
            case "infinite-size": pdf.DisplayWidth = double.PositiveInfinity; break;
            case "partial-size": pdf.DisplayWidth = 0; break;
            case "negative-size": pdf.DisplayHeight = -1; break;
            case "legacy-offset": pdf.DisplayWidth = pdf.DisplayHeight = 0; break;
            case "outside-x": pdf.OffsetX = 501; break;
            case "outside-y": pdf.OffsetY = 701; break;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(changed));
        Assert.Equal(original.Title, (await repository.LoadAsync(original.Id))!.Title);
        AssertPlacement(original.Pages[0], (await repository.LoadAsync(original.Id))!.Pages[0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => backups.ExportAsync(path, [changed]));
        Assert.Equal(backupBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Root, "*.tmp"));
    }

    [Fact]
    public async Task CorruptStoredPlacementIsRejectedByNotebookAndSearchLoads()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var document = Sample(new string('a', 64));
        await repository.SaveAsync(document);
        using (var connection = Open(directory.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE pages SET metadata_json=json_set(metadata_json,'$.pdf.displayWidth',0)";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(document.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadSearchDocumentsAsync());
    }

    [Fact]
    public async Task InterruptedDraftPreservesExpandedPdfWhenRecoveredAsCopy()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var asset = await repository.PutAssetAsync("original.pdf", "application/pdf", [2, 7, 1]);
        var original = Sample(asset.Id);
        var draftPath = Path.Combine(directory.Root, "drafts");
        await new DraftRecoveryStore(draftPath).WriteAsync(original, 17);

        var reopened = new DraftRecoveryStore(draftPath);
        var file = Assert.Single(await reopened.ReadAsync());
        Assert.Equal(1, file.Draft.Version);
        AssertPlacement(original.Pages[0], file.Draft.Document.Pages[0]);
        var recovered = MainWindow.MakeRecoveryCopy(file.Draft.Document);
        Assert.NotEqual(original.Id, recovered.Id);
        Assert.NotEqual(original.Pages[0].Id, recovered.Pages[0].Id);
        AssertPlacement(original.Pages[0], recovered.Pages[0]);
        await repository.SaveAsync(recovered);
        AssertPlacement(original.Pages[0], (await repository.LoadAsync(recovered.Id))!.Pages[0]);
        Assert.True(await reopened.ForgetAsync(file));
        Assert.Empty(await reopened.ReadAsync());
    }

    [Fact]
    public async Task PaperGuidePlacementSurvivesSnapshotsHistorySqliteBackupAndDraftRecovery()
    {
        using var sourceDirectory = new StorageTestDirectory();
        using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        var document = PatternedSample();
        var initial = document.Snapshot();
        var history = new NotebookHistory(); history.Reset(document);
        document.Pages[0].Width += 30;
        document.Pages[0].PaperLayout!.X += 30;
        history.Record(document);
        Assert.Equal(initial.Pages[0].PaperLayout, history.Undo()!.Pages[0].PaperLayout);
        Assert.Equal(document.Pages[0].PaperLayout, history.Redo()!.Pages[0].PaperLayout);
        Assert.NotSame(initial.Pages[0].PaperLayout, document.Pages[0].PaperLayout);
        AssertPlacement(document.Pages[0], JsonSerializer.Deserialize<NotePage>(
            JsonSerializer.Serialize(document.Pages[0], DocumentJson.Options), DocumentJson.Options)!);

        await source.SaveAsync(document);
        using var reopened = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        var saved = (await reopened.LoadAsync(document.Id))!;
        AssertPlacement(document.Pages[0], saved.Pages[0]);
        AssertPlacement(document.Pages[0], Assert.Single(await reopened.LoadSearchDocumentsAsync()).Pages[0]);
        var path = Path.Combine(sourceDirectory.Root, "paper-guides.moye");
        await new BackupService(source).ExportAsync(path, [saved]);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var restored = Assert.Single(await new BackupService(destination).ImportAsync(path));
        AssertPlacement(saved.Pages[0], restored.Pages[0]);
        await destination.SaveAsync(restored);
        AssertPlacement(saved.Pages[0], (await destination.LoadAsync(restored.Id))!.Pages[0]);

        var draftPath = Path.Combine(sourceDirectory.Root, "drafts");
        await new DraftRecoveryStore(draftPath).WriteAsync(document, 21);
        var draft = Assert.Single(await new DraftRecoveryStore(draftPath).ReadAsync()).Draft;
        AssertPlacement(document.Pages[0], MainWindow.MakeRecoveryCopy(draft.Document).Pages[0]);
    }

    [Theory]
    [InlineData("paperLayout")]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("outside-canvas")]
    [InlineData("empty-size")]
    public async Task V4BackupRejectsMissingOrInvalidPaperLayoutBeforeImportingAssets(string corruption)
    {
        using var sourceDirectory = new StorageTestDirectory();
        using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("note.png", "image/png", [8, 6, 7]);
        var document = PatternedSample(); document.Pages[0].Images = [new NoteImage { AssetId = asset.Id }];
        var path = Path.Combine(sourceDirectory.Root, "damaged-paper.moye");
        await new BackupService(source).ExportAsync(path, [document]);
        RewriteBackup(path, root =>
        {
            var page = root["pages"]![0]!.AsObject();
            if (corruption == "paperLayout") page.Remove("paperLayout");
            else
            {
                var layout = page["paperLayout"]!.AsObject();
                if (corruption == "outside-canvas") layout["x"] = 9999;
                else if (corruption == "empty-size") layout["width"] = 0;
                else layout.Remove(corruption);
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(destination).ImportAsync(path));
        Assert.Empty(await destination.ListAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => destination.GetAssetAsync(asset.Id));
    }

    [Theory]
    [InlineData("negative-x")]
    [InlineData("negative-y")]
    [InlineData("nan-position")]
    [InlineData("infinite-size")]
    [InlineData("empty-size")]
    [InlineData("negative-size")]
    [InlineData("outside-x")]
    [InlineData("outside-y")]
    public async Task InvalidPaperLayoutCannotOverwriteSavedNotebookOrBackup(string invalid)
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var original = PatternedSample();
        await repository.SaveAsync(original);
        var path = Path.Combine(directory.Root, "valid-paper.moye");
        var backups = new BackupService(repository);
        await backups.ExportAsync(path, [original]);
        var backupBytes = await File.ReadAllBytesAsync(path);
        var changed = original.Snapshot(); changed.Title = "Must roll back";
        var paper = changed.Pages[0].PaperLayout!;
        switch (invalid)
        {
            case "negative-x": paper.X = -1; break;
            case "negative-y": paper.Y = -1; break;
            case "nan-position": paper.Y = double.NaN; break;
            case "infinite-size": paper.Width = double.PositiveInfinity; break;
            case "empty-size": paper.Width = 0; break;
            case "negative-size": paper.Height = -1; break;
            case "outside-x": paper.X = 501; break;
            case "outside-y": paper.Y = 701; break;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(changed));
        Assert.Equal(original.Title, (await repository.LoadAsync(original.Id))!.Title);
        AssertPlacement(original.Pages[0], (await repository.LoadAsync(original.Id))!.Pages[0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => backups.ExportAsync(path, [changed]));
        Assert.Equal(backupBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CorruptStoredPaperLayoutIsRejectedByNotebookAndSearchLoads()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var document = PatternedSample();
        await repository.SaveAsync(document);
        using (var connection = Open(directory.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE pages SET metadata_json=json_set(metadata_json,'$.paperLayout.width',0)";
            command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(document.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadSearchDocumentsAsync());
    }

    private static NotebookDocument PatternedSample()
    {
        var document = Sample("");
        document.Pages[0].Pdf = null;
        document.Pages[0].Template = PaperTemplate.Cornell;
        document.Pages[0].PaperLayout = new PaperPageLayout { X = 120, Y = 80, Width = 600, Height = 800 };
        return document;
    }

    private static NotebookDocument Sample(string assetId) => new()
    {
        Title = "Expanded PDF", Pages = [new NotePage
        {
            Width = 1100, Height = 1500,
            Pdf = new PdfPageSource { AssetId = assetId, PageIndex = 2, Rotation = 90, CropX = 20, CropY = 30, CropWidth = 600, CropHeight = 800,
                OffsetX = 120, OffsetY = 80, DisplayWidth = 600, DisplayHeight = 800 },
            Texts = [new NoteText { X = 840, Y = 1100, Width = 150, Height = 200, Text = "More room for notes" }]
        }]
    };

    private static void AssertPlacement(NotePage expected, NotePage actual)
    {
        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Pdf, actual.Pdf);
        Assert.Equal(expected.Template, actual.Template);
        Assert.Equal(expected.PaperLayout, actual.PaperLayout);
        Assert.Equal(expected.Texts[0] with { Id = actual.Texts[0].Id }, actual.Texts[0]);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }

    private static void RemovePlacement(JsonObject pdf)
    {
        foreach (var property in new[] { "offsetX", "offsetY", "displayWidth", "displayHeight" }) pdf.Remove(property);
    }

    private static void RewriteBackup(string path, Action<JsonNode> edit, int? version = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var document = ReadJson(archive.GetEntry("notebooks/000000.json")!);
        edit(document);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        Replace(archive, "notebooks/000000.json", bytes);
        var manifest = ReadJson(archive.GetEntry("manifest.json")!);
        if (version.HasValue) manifest["version"] = version.Value;
        manifest["notebooks"]![0]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Replace(archive, "manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }

    private static JsonNode ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open(); return JsonNode.Parse(stream)!;
    }

    private static void Replace(ZipArchive archive, string path, byte[] bytes)
    {
        archive.GetEntry(path)!.Delete();
        using var stream = archive.CreateEntry(path).Open(); stream.Write(bytes);
    }
}
