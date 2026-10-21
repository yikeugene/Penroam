using System.ComponentModel;
using System.Windows;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.ViewModels;

public sealed class SectionViewModel : ObservableObject
{
    public SectionViewModel(NoteSection section, int number, int pageCount)
    {
        Section = section; Number = number; PageCount = pageCount;
        PropertyChangedEventManager.AddHandler(L.Current, OnLanguageChanged, nameof(L.LanguageCode));
    }

    public NoteSection Section { get; }
    public string Id => Section.Id;
    public string Title => Section.Title;
    public int Number { get; }
    public int PageCount { get; }
    public string PageCountText => L.Text(PageCount == 1 ? "Messages.Count.Page" : "Messages.Count.Pages", PageCount);

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Notify(nameof(PageCountText));
}
