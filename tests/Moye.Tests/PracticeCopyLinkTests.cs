using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class PracticeCopyLinkTests
{
    [Fact]
    public void InsertedQuestionSetKeepsInternalSourceLinksAndSharesANewGroup()
    {
        var source = Sample(new string('a', 64));
        var section = new NoteSection(); var target = new NotebookDocument { Sections = [section] };
        var copies = DocumentWorkflows.InsertPages(target, source.Pages, section.Id);
        AssertCopyLinks(source, copies);
    }

    [Fact]
    public void OrganizerCopiesBothQuestionAndAttemptsIntoAnIndependentGroup()
    {
        var source = Sample(new string('b', 64)); var before = source.Snapshot();
        var ids = PageOrganization.Duplicate(source, source.Pages.Select(page => page.Id).ToArray());
        var copies = ids.Select(id => source.Pages.Single(page => page.Id == id)).ToArray();
        AssertCopyLinks(before, copies);
        Assert.Equal(before.Pages[1].Practice, source.Pages.Single(page => page.Id == before.Pages[1].Id).Practice);
    }

    [Fact]
    public void CopyingOnlyAnAttemptRetainsItsExistingSourceAndGroup()
    {
        var book = Sample(new string('c', 64)); var attempt = book.Pages[1];
        var id = Assert.Single(PageOrganization.Duplicate(book, [attempt.Id]));
        var copied = book.Pages.Single(page => page.Id == id);
        Assert.Equal(attempt.Practice!.SourcePageId, copied.Practice!.SourcePageId);
        Assert.Equal(attempt.Practice.GroupId, copied.Practice.GroupId); Assert.NotSame(attempt.Practice, copied.Practice);
    }

    [Fact]
    public async Task CrossNotebookCopyRemapsSourcesAndKeepsMetadataOnlyQuestionAsset()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var asset = await model.Repository.PutAssetAsync("question.png", "image/png", [1, 4, 7]);
        var source = Sample(asset.Id); var section = new NoteSection();
        var target = new NotebookDocument { Sections = [section], Pages = [new() { SectionId = section.Id }] };
        await model.Repository.SaveAsync(source); await model.Repository.SaveAsync(target); await model.OpenAsync(source.Id);
        await model.TransferPagesAsync(source.Pages.Select(page => page.Id), target.Id, section.Id, true);
        var saved = (await model.Repository.LoadAsync(target.Id))!;
        var copies = saved.Pages.Where(page => page.Title is "Question" or "Attempt 1" or "Attempt 2").ToArray();
        AssertCopyLinks(source, copies);
        Assert.Equal(asset.Bytes, (await model.Repository.GetAssetAsync(copies[1].Practice!.QuestionAssetId)).Bytes);
        Assert.Equal(source.Pages.Select(page => page.Id), (await model.Repository.LoadAsync(source.Id))!.Pages.Select(page => page.Id));
    }

    [Fact]
    public async Task CrossNotebookMoveWithoutIdCollisionsKeepsExistingGroupAndSource()
    {
        using var directory = new StorageTestDirectory(); using var model = new MainViewModel(new SqliteNotebookRepository(directory.DatabasePath));
        var asset = await model.Repository.PutAssetAsync("question.png", "image/png", [2, 4, 6]);
        var source = Sample(asset.Id); var section = new NoteSection(); var target = new NotebookDocument { Sections = [section] };
        await model.Repository.SaveAsync(source); await model.Repository.SaveAsync(target); await model.OpenAsync(source.Id);
        await model.TransferPagesAsync(source.Pages.Select(page => page.Id), target.Id, section.Id, false);
        var saved = (await model.Repository.LoadAsync(target.Id))!;
        Assert.Equal(source.Pages.Select(page => page.Id), saved.Pages.Select(page => page.Id));
        Assert.Equal(source.Pages[1].Practice, saved.Pages[1].Practice);
    }

    private static void AssertCopyLinks(NotebookDocument original, IReadOnlyList<NotePage> copies)
    {
        Assert.Equal(3, copies.Count); Assert.NotEqual(original.Pages[0].Id, copies[0].Id);
        Assert.Equal(copies[0].Id, copies[1].Practice!.SourcePageId); Assert.Equal(copies[0].Id, copies[2].Practice!.SourcePageId);
        Assert.Equal(copies[1].Practice!.GroupId, copies[2].Practice!.GroupId);
        Assert.NotEqual(original.Pages[1].Practice!.GroupId, copies[1].Practice!.GroupId);
        Assert.Equal(original.Pages[0].Id, original.Pages[1].Practice!.SourcePageId);
        Assert.Equal(original.Pages[1].Practice!.QuestionAssetId, copies[1].Practice!.QuestionAssetId);
    }
    private static NotebookDocument Sample(string assetId)
    {
        var section = new NoteSection(); var source = new NotePage { SectionId = section.Id, Title = "Question" };
        var attempt = new PracticeAttempt { SourcePageId = source.Id, QuestionAssetId = assetId, Question = "Example 3", QuestionWidth = 200, QuestionHeight = 100 };
        return new() { Sections = [section], Pages = [source,
            new() { SectionId = section.Id, Title = "Attempt 1", Practice = attempt },
            new() { SectionId = section.Id, Title = "Attempt 2", Practice = attempt with { Number = 2 } }] };
    }
}
