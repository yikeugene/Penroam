using System.IO;
using System.Text.Json;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class StudyWorkflowTests
{
    [Fact]
    public void SnapshotAndJsonKeepQuestionExplanationsResumeHintsAndIndependentAnchors()
    {
        var page = StudyPage();
        var snapshot = page.Snapshot();
        var roundTrip = JsonSerializer.Deserialize<NotePage>(JsonSerializer.Serialize(page, DocumentJson.Options), DocumentJson.Options)!;
        Assert.Equal(page.StudyQuestions, snapshot.StudyQuestions);
        Assert.Equal(page.StudyQuestions, roundTrip.StudyQuestions);
        Assert.Equal(page.ResumeHint, roundTrip.ResumeHint);
        snapshot.StudyQuestions[0].Anchor!.X = 100;
        snapshot.StudyQuestions[0].Explanation = "changed";
        snapshot.ResumeHint!.Anchor!.Y = 200;
        Assert.Equal(10, page.StudyQuestions[0].Anchor!.X);
        Assert.Equal("Reverse the substitution", page.StudyQuestions[0].Explanation);
        Assert.Equal(20, page.ResumeHint!.Anchor!.Y);
        var legacy = JsonSerializer.Deserialize<NotePage>("{}", DocumentJson.Options)!;
        Assert.Empty(legacy.StudyQuestions); Assert.Null(legacy.ResumeHint);
    }

    [Fact]
    public void InboxSpansSectionsFiltersResolvedAndFindsExplanationWithoutAliasing()
    {
        var first = new NoteSection { Title = "Mathematics" };
        var second = new NoteSection { Title = "Science" };
        var page = StudyPage(); page.SectionId = second.Id; page.Title = "Substitution";
        var closed = new StudyQuestion { Prompt = "Solved already", Status = StudyQuestionStatus.Resolved };
        page.StudyQuestions.Add(closed);
        var doc = new NotebookDocument { Sections = [first, second], Pages = [new() { SectionId = first.Id }, page] };
        var item = Assert.Single(StudyWorkflowService.Items(doc));
        Assert.Contains("Science · Page 1 · Substitution", item.Location);
        Assert.Equal(page.Id, item.PageId); Assert.Equal(second.Id, item.SectionId);
        Assert.Equal("Need help", item.Detail);
        Assert.Single(StudyWorkflowService.Items(doc, query: "REVERSE"));
        Assert.Single(StudyWorkflowService.Items(doc, query: "independently"));
        Assert.Empty(StudyWorkflowService.Items(doc, query: "not present"));
        Assert.Equal(2, StudyWorkflowService.Items(doc, includeResolved: true).Count);
        item.Question!.Anchor!.X = 77;
        Assert.Equal(10, page.StudyQuestions[0].Anchor!.X);
        var resume = Assert.Single(StudyWorkflowService.Items(doc, resumeOnly: true));
        Assert.Equal("Try example 3 without the worked answer", resume.Heading);
        Assert.Equal(page.Id, resume.PageId);
    }

    [Fact]
    public void AddUpdateResumeAndDeleteAreSeparateUndoStepsAndTargetTheRightSection()
    {
        using var model = new MainViewModel(new WorkflowRepository());
        var first = new NoteSection(); var second = new NoteSection { Title = "Questions" };
        var page = new NotePage { SectionId = second.Id };
        model.ReplaceDocument(new NotebookDocument { Sections = [first, second], Pages = [new() { SectionId = first.Id }, page] }, true);
        var question = new StudyQuestion { Prompt = "Why does the sign change?", Anchor = new() { X = 10, Y = 20, Width = 30, Height = 40 } };
        Assert.True(model.AddStudyQuestion(page.Id, question));
        Assert.Empty(page.StudyQuestions);
        Assert.Equal(second.Id, model.SelectedSection!.Id);
        Assert.Equal(page.Id, model.SelectedPage!.Page.Id);
        question.Prompt = "caller mutation"; question.Anchor!.X = 999;
        Assert.Equal("Why does the sign change?", model.SelectedPage.Page.StudyQuestions[0].Prompt);
        Assert.Equal(10, model.SelectedPage.Page.StudyQuestions[0].Anchor!.X);
        var understood = model.SelectedPage.Page.StudyQuestions[0] with { Status = StudyQuestionStatus.Understood, Explanation = "Multiplying by a negative number" };
        Assert.True(model.UpdateStudyQuestion(page.Id, understood));
        Assert.True(model.SetResumeHint(page.Id, new() { NextStep = "Try this independently" }));
        Assert.True(model.RemoveStudyQuestion(page.Id, understood.Id));
        Assert.Empty(model.SelectedPage.Page.StudyQuestions);
        model.Undo(); Assert.Equal(StudyQuestionStatus.Understood, Assert.Single(model.SelectedPage!.Page.StudyQuestions).Status);
        model.Undo(); Assert.Null(model.SelectedPage!.Page.ResumeHint);
        model.Undo(); Assert.Equal(StudyQuestionStatus.NeedHelp, Assert.Single(model.SelectedPage!.Page.StudyQuestions).Status);
        model.Undo(); Assert.Empty(model.SelectedPage!.Page.StudyQuestions); Assert.False(model.CanUndo);
        model.Redo(); Assert.Single(model.SelectedPage!.Page.StudyQuestions);
    }

    [Fact]
    public void NoOpInvalidAndBusyEditsDoNotCreateHistoryOrChangeThePage()
    {
        using var model = new MainViewModel(new WorkflowRepository());
        var page = StudyPage(); model.ReplaceDocument(new NotebookDocument { Pages = [page] }, true);
        var before = JsonSerializer.Serialize(model.Document, DocumentJson.Options);
        Assert.False(model.UpdateStudyQuestion(page.Id, StudyWorkflowService.Copy(page.StudyQuestions[0])));
        Assert.False(model.SetResumeHint(page.Id, StudyWorkflowService.Copy(page.ResumeHint!)));
        Assert.False(model.AddStudyQuestion(page.Id, new() { Prompt = " " }));
        Assert.False(model.AddStudyQuestion(page.Id, page.StudyQuestions[0]));
        Assert.False(model.UpdateStudyQuestion(page.Id, page.StudyQuestions[0] with { Status = (StudyQuestionStatus)500 }));
        Assert.False(model.SetResumeHint(page.Id, new() { NextStep = "too far", Anchor = new() { X = 100000, Width = 10, Height = 10 } }));
        Assert.False(model.RemoveStudyQuestion(page.Id, "missing"));
        Assert.False(model.AddStudyQuestion("missing", new() { Prompt = "Question" }));
        model.IsBusy = true;
        Assert.False(model.SetResumeHint(page.Id, null));
        Assert.False(model.RemoveStudyQuestion(page.Id, page.StudyQuestions[0].Id));
        Assert.False(model.AddStudyQuestion(page.Id, new() { Prompt = "Question" }));
        Assert.False(model.CanUndo);
        Assert.Equal(before, JsonSerializer.Serialize(model.Document, DocumentJson.Options));
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("null-question")]
    [InlineData("duplicate-id")]
    [InlineData("empty-prompt")]
    [InlineData("long-notes")]
    [InlineData("invalid-status")]
    [InlineData("nan-anchor")]
    [InlineData("outside-anchor")]
    [InlineData("empty-resume")]
    public void InvalidStudyMetadataIsRejected(string corruption)
    {
        var page = StudyPage();
        switch (corruption)
        {
            case "null-list": page.StudyQuestions = null!; break;
            case "null-question": page.StudyQuestions.Add(null!); break;
            case "duplicate-id": page.StudyQuestions.Add(page.StudyQuestions[0] with {}); break;
            case "empty-prompt": page.StudyQuestions[0].Prompt = " "; break;
            case "long-notes": page.StudyQuestions[0].RetryNotes = new string('a', StudyWorkflowService.MaximumNotesLength + 1); break;
            case "invalid-status": page.StudyQuestions[0].Status = (StudyQuestionStatus)(-1); break;
            case "nan-anchor": page.StudyQuestions[0].Anchor!.X = double.NaN; break;
            case "outside-anchor": page.ResumeHint!.Anchor!.Width = page.Width; break;
            case "empty-resume": page.ResumeHint!.NextStep = ""; break;
        }
        Assert.False(StudyWorkflowService.IsValid(page));
    }

    [Fact]
    public void ExtendingPageKeepsQuestionAndResumeAnchorsOnTheirOriginalContent()
    {
        var original = StudyPage();
        var extended = PageExtensionService.Extend(original, new(50, 70, 20, 10));
        Assert.Equal(original.StudyQuestions[0].Anchor! with { X = 60, Y = 90 }, extended.StudyQuestions[0].Anchor);
        Assert.Equal(original.ResumeHint!.Anchor! with { X = 60, Y = 90 }, extended.ResumeHint!.Anchor);
        Assert.Equal(10, original.StudyQuestions[0].Anchor!.X); Assert.Equal(20, original.ResumeHint!.Anchor!.Y);
        Assert.True(StudyWorkflowService.IsValid(extended));
    }

    [Fact]
    public async Task StudyMetadataSurvivesDatabaseSearchAndEditableBackup()
    {
        using var directory = new StorageTestDirectory();
        using var destinationDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(directory.DatabasePath);
        var doc = new NotebookDocument { Pages = [StudyPage()] };
        await source.SaveAsync(doc);
        using var reopened = new SqliteNotebookRepository(directory.DatabasePath);
        var loaded = (await reopened.LoadAsync(doc.Id))!;
        Assert.Equal(doc.Pages[0].StudyQuestions, loaded.Pages[0].StudyQuestions);
        Assert.Equal(doc.Pages[0].ResumeHint, loaded.Pages[0].ResumeHint);
        var search = Assert.Single(await reopened.LoadSearchDocumentsAsync());
        Assert.Equal(doc.Pages[0].StudyQuestions, search.Pages[0].StudyQuestions);
        var path = Path.Combine(directory.Root, "study.moye");
        await new BackupService(reopened).ExportAsync(path, [loaded]);
        using var destination = new SqliteNotebookRepository(destinationDirectory.DatabasePath);
        var imported = Assert.Single(await new BackupService(destination).ImportAsync(path));
        Assert.Equal(doc.Pages[0].StudyQuestions, imported.Pages[0].StudyQuestions);
        Assert.Equal(doc.Pages[0].ResumeHint, imported.Pages[0].ResumeHint);
        Assert.True(StudyWorkflowService.IsValid(imported.Pages[0]));
    }

    [Fact]
    public async Task InvalidStudyDataCannotOverwriteAValidNotebookOrBackup()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var doc = new NotebookDocument { Pages = [StudyPage()] }; await repository.SaveAsync(doc);
        var path = Path.Combine(directory.Root, "study.moye"); var backup = new BackupService(repository);
        await backup.ExportAsync(path, [doc]); var before = await File.ReadAllBytesAsync(path);
        var invalid = doc.Snapshot(); invalid.Pages[0].StudyQuestions[0].Status = (StudyQuestionStatus)300;
        await Assert.ThrowsAsync<InvalidDataException>(() => repository.SaveAsync(invalid));
        await Assert.ThrowsAsync<InvalidDataException>(() => backup.ExportAsync(path, [invalid]));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Equal(StudyQuestionStatus.NeedHelp, (await repository.LoadAsync(doc.Id))!.Pages[0].StudyQuestions[0].Status);
    }

    internal static NotePage StudyPage() => new()
    {
        StudyQuestions = [new() { Prompt = "Why does the substitution work?", Explanation = "Reverse the substitution", RetryNotes = "Solve example 3 independently",
            Anchor = new() { X = 10, Y = 20, Width = 100, Height = 120 } }],
        ResumeHint = new() { NextStep = "Try example 3 without the worked answer", Anchor = new() { X = 10, Y = 20, Width = 100, Height = 120 } }
    };
}
