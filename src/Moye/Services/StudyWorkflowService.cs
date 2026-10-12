using Moye.Models;

namespace Moye.Services;

public sealed record StudyInboxItem(string PageId, string SectionId, string Location, string Heading,
    string Detail, DateTimeOffset ModifiedUtc, StudyQuestion? Question, ResumeHint? Resume)
{
    public StudyAnchor? Anchor => Question?.Anchor ?? Resume?.Anchor;
    public string Identity => PageId + ":" + (Question?.Id ?? "resume");
}

public static class StudyWorkflowService
{
    public const int MaximumPromptLength = 2000;
    public const int MaximumNotesLength = 16000;
    public const int MaximumResumeLength = 4000;
    public const int MaximumQuestionsPerPage = 10000;

    public static string StatusLabel(StudyQuestionStatus status) => status switch
    {
        StudyQuestionStatus.NeedHelp => "Need help",
        StudyQuestionStatus.Understood => "Understood",
        StudyQuestionStatus.TryAgain => "Try again",
        StudyQuestionStatus.Resolved => "Resolved",
        _ => "Unknown"
    };

    public static bool IsValid(NotePage page)
    {
        if (page.StudyQuestions is null || page.StudyQuestions.Count > MaximumQuestionsPerPage) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in page.StudyQuestions)
            if (question is null || string.IsNullOrWhiteSpace(question.Id) || question.Id.Length > 160 || !ids.Add(question.Id)
                || !RequiredText(question.Prompt, MaximumPromptLength) || !OptionalText(question.Explanation, MaximumNotesLength)
                || !OptionalText(question.RetryNotes, MaximumNotesLength) || !Enum.IsDefined(question.Status)
                || !IsValidAnchor(page, question.Anchor)) return false;
        return page.ResumeHint is not { } hint || (RequiredText(hint.NextStep, MaximumResumeLength) && IsValidAnchor(page, hint.Anchor));
    }

    public static bool IsValidAnchor(NotePage page, StudyAnchor? anchor) => anchor is null ||
        (double.IsFinite(anchor.X) && double.IsFinite(anchor.Y) && double.IsFinite(anchor.Width) && double.IsFinite(anchor.Height)
        && anchor.X >= 0 && anchor.Y >= 0 && anchor.Width > 0 && anchor.Height > 0
        && anchor.X + anchor.Width <= page.Width + .000001 && anchor.Y + anchor.Height <= page.Height + .000001);

    private static bool RequiredText(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text.Length <= limit;
    private static bool OptionalText(string? text, int limit) => text is not null && text.Length <= limit;
    public static StudyQuestion Copy(StudyQuestion value) => value with { Anchor = value.Anchor is null ? null : value.Anchor with {} };
    public static ResumeHint Copy(ResumeHint value) => value with { Anchor = value.Anchor is null ? null : value.Anchor with {} };

    /// <summary>Use on a detached page after extending its dimensions.</summary>
    public static void TranslateAnchors(NotePage page, double x, double y)
    {
        foreach (var question in page.StudyQuestions)
            if (question.Anchor is { } anchor) question.Anchor = anchor with { X = anchor.X + x, Y = anchor.Y + y };
        if (page.ResumeHint?.Anchor is { } resume) page.ResumeHint.Anchor = resume with { X = resume.X + x, Y = resume.Y + y };
    }

    public static IReadOnlyList<StudyInboxItem> Items(NotebookDocument document, bool resumeOnly = false,
        bool includeResolved = false, string? query = null)
    {
        var items = new List<StudyInboxItem>();
        var sectionNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var page in document.Pages)
        {
            var number = sectionNumbers.GetValueOrDefault(page.SectionId) + 1; sectionNumbers[page.SectionId] = number;
            var section = document.Sections.FirstOrDefault(s => s.Id == page.SectionId)?.Title ?? "General";
            var location = $"{section} · Page {number}" + (string.IsNullOrWhiteSpace(page.Title) ? "" : $" · {page.Title}");
            if (resumeOnly)
            {
                if (page.ResumeHint is { } hint)
                    items.Add(new(page.Id, page.SectionId, location, hint.NextStep, "Next time, continue here", hint.ModifiedUtc, null, Copy(hint)));
            }
            else
            {
                foreach (var question in page.StudyQuestions.Where(q => includeResolved || q.Status != StudyQuestionStatus.Resolved))
                    items.Add(new(page.Id, page.SectionId, location, question.Prompt, StatusLabel(question.Status), question.ModifiedUtc, Copy(question), null));
            }
        }
        var term = query?.Trim() ?? "";
        return items.Where(item => term.Length == 0 || new[] { item.Location, item.Heading, item.Detail, item.Question?.Explanation, item.Question?.RetryNotes }
            .Any(text => text?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true))
            .OrderByDescending(item => item.ModifiedUtc).ToArray();
    }
}
