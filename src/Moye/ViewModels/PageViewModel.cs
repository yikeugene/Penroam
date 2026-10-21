using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.ViewModels;

public sealed class PageViewModel : ObservableObject
{
    public PageViewModel(NotePage page, int number, double zoom)
    {
        Page = page; Number = number; _zoom = zoom;
        PropertyChangedEventManager.AddHandler(L.Current, OnLanguageChanged, nameof(L.LanguageCode));
    }

    public NotePage Page { get; }
    public int Number { get; }
    private double _zoom;
    private BitmapSource? _thumbnail;
    public double Zoom { get => _zoom; set { if (Set(ref _zoom, value)) { Notify(nameof(DisplayWidth)); Notify(nameof(DisplayHeight)); } } }
    public double DisplayWidth => Page.Width * Zoom;
    public double DisplayHeight => Page.Height * Zoom;
    public string Caption => $"{(Page.IsBookmarked ? "★ " : "")}{Number:D2}  ·  {(string.IsNullOrWhiteSpace(Page.Title) ? PaperCaption : Page.Title)}";
    public string PaperCaption => L.Text(Page.Pdf is not null ? "Messages.Paper.Pdf" : Page.Template switch
    {
        PaperTemplate.Grid => "Messages.Paper.Grid", PaperTemplate.Ruled => "Messages.Paper.Ruled",
        PaperTemplate.DotGrid => "Messages.Paper.DotGrid", PaperTemplate.Cornell => "Messages.Paper.Cornell",
        PaperTemplate.Graph => "Messages.Paper.Graph", PaperTemplate.Meeting => "Messages.Paper.Meeting", _ => "Messages.Paper.Blank"
    });
    public BitmapSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        Notify(nameof(PaperCaption)); Notify(nameof(Caption));
    }
}
