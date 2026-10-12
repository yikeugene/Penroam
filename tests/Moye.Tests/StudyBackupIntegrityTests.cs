using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class StudyBackupIntegrityTests
{
    [Theory]
    [InlineData("studyQuestions")]
    [InlineData("resumeHint")]
    [InlineData("practice")]
    [InlineData("studyQuestions.0.id")]
    [InlineData("studyQuestions.0.prompt")]
    [InlineData("studyQuestions.0.explanation")]
    [InlineData("studyQuestions.0.retryNotes")]
    [InlineData("studyQuestions.0.status")]
    [InlineData("studyQuestions.0.anchor")]
    [InlineData("studyQuestions.0.createdUtc")]
    [InlineData("studyQuestions.0.modifiedUtc")]
    [InlineData("studyQuestions.0.anchor.x")]
    [InlineData("studyQuestions.0.anchor.width")]
    [InlineData("resumeHint.nextStep")]
    [InlineData("resumeHint.modifiedUtc")]
    [InlineData("resumeHint.anchor")]
    [InlineData("resumeHint.anchor.y")]
    [InlineData("resumeHint.anchor.height")]
    [InlineData("practice.sourcePageId")]
    [InlineData("practice.questionWidth")]
    [InlineData("practice.questionHeight")]
    [InlineData("practice.questionAssetId")]
    [InlineData("practice.reflection")]
    [InlineData("practice.reviewed")]
    [InlineData("texts.0.isFreeFlow")]
    [InlineData("texts.0.lineSpacing")]
    public async Task MissingV5FieldsFailBeforeWritingAssets(string field)
    {
        using var sourceDirectory = new StorageTestDirectory(); using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("question.png", "image/png", [3, 2, 1]);
        var path = Path.Combine(sourceDirectory.Root, "damaged.moye");
        await new BackupService(source).ExportAsync(path, [Sample(asset.Id)]);
        Rewrite(path, page => Remove(page, field));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(destination).ImportAsync(path));
        Assert.Empty(await destination.ListAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => destination.GetAssetAsync(asset.Id));
    }

    [Theory]
    [InlineData("studyQuestions")]
    [InlineData("studyQuestions.0")]
    [InlineData("studyQuestions.0.prompt")]
    [InlineData("studyQuestions.0.explanation")]
    [InlineData("studyQuestions.0.retryNotes")]
    [InlineData("resumeHint.nextStep")]
    public async Task NullNonnullableStudyDataFailsBeforeWritingAssets(string field)
    {
        using var sourceDirectory = new StorageTestDirectory(); using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(sourceDirectory.DatabasePath);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("question.png", "image/png", [6, 2, 4]);
        var path = Path.Combine(sourceDirectory.Root, "null-study.moye");
        await new BackupService(source).ExportAsync(path, [Sample(asset.Id)]);
        Rewrite(path, page => SetNull(page, field));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService(destination).ImportAsync(path));
        await Assert.ThrowsAsync<FileNotFoundException>(() => destination.GetAssetAsync(asset.Id));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task V1ThroughV4RestoreWithoutNewFieldsUsingLegacyDefaults(int version)
    {
        using var directory = new StorageTestDirectory(); using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var doc = new NotebookDocument { Pages = [new() { Texts = [new() { Text = "Legacy typed note" }] }] };
        var path = Path.Combine(directory.Root, "legacy.moye"); var backup = new BackupService(repository);
        await backup.ExportAsync(path, [doc]);
        Rewrite(path, page =>
        {
            foreach (var name in new[] { "studyQuestions", "resumeHint", "practice" }) page.AsObject().Remove(name);
            foreach (var text in page["texts"]!.AsArray()) { text!.AsObject().Remove("isFreeFlow"); text.AsObject().Remove("lineSpacing"); }
        }, version);
        var restored = Assert.Single(await backup.ImportAsync(path)); var page = Assert.Single(restored.Pages);
        Assert.Empty(page.StudyQuestions); Assert.Null(page.ResumeHint); Assert.Null(page.Practice);
        Assert.False(page.Texts[0].IsFreeFlow); Assert.Equal(0, page.Texts[0].LineSpacing); Assert.Equal("Legacy typed note", page.Texts[0].Text);
    }

    [Fact]
    public async Task V5AllowsExplicitlyEmptyStudyStateAndNullAnchors()
    {
        using var directory = new StorageTestDirectory(); using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var withQuestion = StudyWorkflowTests.StudyPage(); withQuestion.StudyQuestions[0].Anchor = null; withQuestion.ResumeHint!.Anchor = null;
        var doc = new NotebookDocument { Pages = [new(), withQuestion] };
        var path = Path.Combine(directory.Root, "nullable.moye"); var backup = new BackupService(repository);
        await backup.ExportAsync(path, [doc]); var restored = Assert.Single(await backup.ImportAsync(path));
        Assert.Empty(restored.Pages[0].StudyQuestions); Assert.Null(restored.Pages[0].ResumeHint); Assert.Null(restored.Pages[0].Practice);
        Assert.Null(restored.Pages[1].StudyQuestions[0].Anchor); Assert.Null(restored.Pages[1].ResumeHint!.Anchor);
    }

    private static NotebookDocument Sample(string assetId)
    {
        var page = StudyWorkflowTests.StudyPage(); page.Texts.Add(new() { Text = "Direct typing", IsFreeFlow = true, LineSpacing = 32 });
        page.Practice = new() { SourcePageId = "original-page", QuestionAssetId = assetId, Question = "Example 3", QuestionWidth = 200, QuestionHeight = 100, Reflection = "Check the sign" };
        return new() { Pages = [page] };
    }
    private static (JsonNode Node, string Property) Parent(JsonNode root, string path)
    {
        var parts = path.Split('.'); var node = root;
        foreach (var part in parts[..^1]) node = int.TryParse(part, out var index) ? node[index]! : node[part]!;
        return (node, parts[^1]);
    }
    private static void Remove(JsonNode page, string path)
    {
        var (parent, property) = Parent(page, path); parent.AsObject().Remove(property);
    }
    private static void SetNull(JsonNode page, string path)
    {
        var (parent, property) = Parent(page, path);
        if (int.TryParse(property, out var index)) parent[index] = null; else parent[property] = null;
    }
    private static void Rewrite(string path, Action<JsonNode> edit, int version = 5)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        JsonNode Read(string entry) { using var stream = archive.GetEntry(entry)!.Open(); return JsonNode.Parse(stream)!; }
        void Write(string entry, byte[] bytes) { archive.GetEntry(entry)!.Delete(); using var stream = archive.CreateEntry(entry).Open(); stream.Write(bytes); }
        var document = Read("notebooks/000000.json"); edit(document["pages"]![0]!);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString()); Write("notebooks/000000.json", bytes);
        var manifest = Read("manifest.json"); manifest["version"] = version;
        manifest["notebooks"]![0]!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Write("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
    }
}
