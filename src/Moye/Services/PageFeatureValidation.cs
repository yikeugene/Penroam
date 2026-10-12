using Moye.Models;

namespace Moye.Services;

public static class PageFeatureValidation
{
    public static bool IsValid(NotePage page)
    {
        if (!StudyWorkflowService.IsValid(page) || page.Texts is null || page.Texts.Any(t => t is null || !double.IsFinite(t.LineSpacing) || t.LineSpacing < 0 || t.LineSpacing > 1024)) return false;
        return page.Practice is not { } p || (!string.IsNullOrWhiteSpace(p.GroupId) && p.GroupId.Length <= 160
            && p.SourcePageId is { Length: > 0 and <= 160 } && p.QuestionAssetId is { Length: 64 } && p.QuestionAssetId.All(Uri.IsHexDigit)
            && double.IsFinite(p.QuestionWidth) && p.QuestionWidth > 0 && p.QuestionWidth <= 19200
            && double.IsFinite(p.QuestionHeight) && p.QuestionHeight > 0 && p.QuestionHeight <= 19200
            && p.Question is { Length: > 0 and <= 1000 } && p.Reflection is { Length: <= 16000 } && p.Number > 0 && p.Number < 1000000);
    }
}