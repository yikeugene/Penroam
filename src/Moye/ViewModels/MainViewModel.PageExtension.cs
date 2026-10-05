using Moye.Models;
using Moye.Services;

namespace Moye.ViewModels;

public sealed partial class MainViewModel
{
    public bool ExtendPage(string pageId, PageMargins margins)
    {
        if (Document is null || IsBusy) return false;
        var index = Document.Pages.FindIndex(page => page.Id == pageId);
        if (index < 0 || !PageExtensionService.CanExtend(Document.Pages[index], margins)) return false;
        var extended = PageExtensionService.Extend(Document.Pages[index], margins);
        Document.Pages[index] = extended;
        RecordChange(true, extended.SectionId, extended.Id);
        return true;
    }
}
