using Moye.Models;
using Moye.Services;

namespace Moye.ViewModels;

public sealed partial class MainViewModel
{
    public bool AddStudyQuestion(string pageId, StudyQuestion question)
    {
        var page = EditableStudyPage(pageId);
        if (page is null || page.StudyQuestions.Any(existing => existing.Id == question.Id)) return false;
        var edited = page.Snapshot();
        var copy = StudyWorkflowService.Copy(question);
        copy.Prompt = copy.Prompt?.Trim() ?? "";
        edited.StudyQuestions.Add(copy);
        return ApplyStudyPage(edited);
    }

    public bool UpdateStudyQuestion(string pageId, StudyQuestion question)
    {
        var page = EditableStudyPage(pageId);
        var original = page?.StudyQuestions.FirstOrDefault(existing => existing.Id == question.Id);
        if (page is null || original is null) return false;
        var copy = StudyWorkflowService.Copy(question);
        copy.Prompt = copy.Prompt?.Trim() ?? ""; copy.CreatedUtc = original.CreatedUtc;
        copy.ModifiedUtc = original.ModifiedUtc;
        if (copy == original) return false;
        copy.ModifiedUtc = DateTimeOffset.UtcNow;
        var edited = page.Snapshot();
        edited.StudyQuestions[edited.StudyQuestions.FindIndex(existing => existing.Id == copy.Id)] = copy;
        return ApplyStudyPage(edited);
    }

    public bool RemoveStudyQuestion(string pageId, string questionId)
    {
        var page = EditableStudyPage(pageId);
        if (page is null || page.StudyQuestions.All(question => question.Id != questionId)) return false;
        var edited = page.Snapshot(); edited.StudyQuestions.RemoveAll(question => question.Id == questionId);
        return ApplyStudyPage(edited);
    }

    public bool SetResumeHint(string pageId, ResumeHint? hint)
    {
        var page = EditableStudyPage(pageId);
        if (page is null) return false;
        var copy = hint is null ? null : StudyWorkflowService.Copy(hint);
        if (copy is not null)
        {
            copy.NextStep = copy.NextStep?.Trim() ?? "";
            copy.ModifiedUtc = page.ResumeHint?.ModifiedUtc ?? copy.ModifiedUtc;
        }
        if (copy == page.ResumeHint) return false;
        if (copy is not null) copy.ModifiedUtc = DateTimeOffset.UtcNow;
        var edited = page.Snapshot(); edited.ResumeHint = copy;
        return ApplyStudyPage(edited);
    }

    private NotePage? EditableStudyPage(string pageId) => IsBusy ? null : Document?.Pages.FirstOrDefault(page => page.Id == pageId);
    private bool ApplyStudyPage(NotePage page)
    {
        if (Document is null || IsBusy || !StudyWorkflowService.IsValid(page)) return false;
        var index = Document.Pages.FindIndex(candidate => candidate.Id == page.Id);
        if (index < 0) return false;
        Document.Pages[index] = page;
        RecordChange(true, page.SectionId, page.Id);
        return true;
    }
}
