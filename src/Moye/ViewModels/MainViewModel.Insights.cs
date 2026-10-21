using System.Text.Json;
using System.Windows;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.ViewModels;

public sealed partial class MainViewModel
{
    public bool HasResumeHints => Document?.Pages.Any(page => page.ResumeHint is not null) == true;
    public string ResumeSummary => L.Text("Messages.Resume.Summary", SelectedPage?.Page.ResumeHint?.NextStep ?? Document?.Pages
        .Where(page => page.ResumeHint is not null).MaxBy(page => page.ResumeHint!.ModifiedUtc)?.ResumeHint?.NextStep ?? "");

    public IReadOnlyList<NotePage> RegionHistory(string pageId) => _history.PageVersions(pageId)
        .Where(old => Document?.Pages.FirstOrDefault(p => p.Id == pageId) is { } current && RegionRestoreService.Compatible(current, old))
        .ToArray();

    public bool RestoreRegion(string pageId, NotePage previous, Rect region)
    {
        if (Document is null || IsBusy) return false;
        var index = Document.Pages.FindIndex(p => p.Id == pageId); if (index < 0) return false;
        var current = Document.Pages[index]; var restored = RegionRestoreService.Restore(current, previous, region);
        if (JsonSerializer.Serialize(current, DocumentJson.Options) == JsonSerializer.Serialize(restored, DocumentJson.Options)) return false;
        Document.Pages[index] = restored; RecordChange(true, restored.SectionId, restored.Id); return true;
    }

    public bool AddPracticeAttempt(string sourcePageId, string assetId, double width, double height, string question, string? groupId = null)
    {
        if (Document is null || IsBusy || Document.Pages.FirstOrDefault(p => p.Id == sourcePageId) is not { } source) return false;
        if (string.IsNullOrWhiteSpace(question) || question.Length > 1000 || groupId is { Length: > 160 } || assetId.Length != 64 || !assetId.All(Uri.IsHexDigit) || width <= 0 || height <= 0 || !double.IsFinite(width) || !double.IsFinite(height)) return false;
        var group = groupId ?? Guid.NewGuid().ToString("N");
        var number = Document.Pages.Where(p => p.Practice?.GroupId == group).Select(p => p.Practice!.Number).DefaultIfEmpty(0).Max() + 1;
        var originalId = source.Practice?.SourcePageId ?? source.Id;
        var page = new NotePage { SectionId = source.SectionId, Template = PaperTemplate.Ruled, Title = $"Attempt {number} · {question}",
            Practice = new() { GroupId = group, SourcePageId = originalId, QuestionAssetId = assetId, Question = question, QuestionWidth = width, QuestionHeight = height, Number = number } };
        var scale = Math.Min(1, Math.Min((page.Width - 96) / width, (page.Height * .48) / height));
        page.Images.Add(new() { AssetId = assetId, X = 48, Y = 48, Width = width * scale, Height = height * scale });
        if (!PageFeatureValidation.IsValid(page)) return false;
        Document.Pages.Insert(Document.Pages.IndexOf(source) + 1, page);
        RecordChange(true, page.SectionId, page.Id); return true;
    }

    public bool SetPracticeReflection(string pageId, string reflection, bool reviewed)
    {
        if (Document is null || IsBusy || reflection.Length > 16000 || Document.Pages.FirstOrDefault(p => p.Id == pageId)?.Practice is not { } attempt) return false;
        if (attempt.Reflection == reflection && attempt.Reviewed == reviewed) return false;
        attempt.Reflection = reflection; attempt.Reviewed = reviewed; RecordChange(false); return true;
    }
}
