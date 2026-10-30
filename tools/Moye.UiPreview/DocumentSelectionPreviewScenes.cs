using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.UiPreview;

/// <summary>Real export controls with synthetic pages, including filters and compact bilingual layouts.</summary>
internal static class DocumentSelectionPreviewScenes
{
    private static readonly string[] ControlIds = ["ExportSectionFilter", "ExportSearchFilter", "ExportRangeFilter",
        "ApplyExportRange", "ResetExportFilters", "ExportBookmarkedFilter", "SelectAllDocumentPages", "ClearDocumentPages", "AcceptDocumentPages"];

    public static void Run(string output)
    {
        var document = CreateNotebook();
        var original = JsonSerializer.Serialize(document, DocumentJson.Options);
        var reports = new List<object>();
        var language = L.Current.LanguageCode;
        try
        {
            foreach (var locale in new[] { "en", "zh-TW" })
            {
                L.SetLanguage(locale);
                var renderer = Renderer();
                var dialog = CreateDialog(document, renderer);
                try
                {
                    if (new WindowInteropHelper(dialog).Handle != IntPtr.Zero)
                        throw new InvalidOperationException("Detached export preview created a native window.");
                    var content = (FrameworkElement)dialog.Content; dialog.Content = null;
                    var list = Find<ListBox>(content, "DocumentPageList");
                    var section = Find<ComboBox>(content, "ExportSectionFilter");
                    var search = Find<TextBox>(content, "ExportSearchFilter");
                    var range = Find<TextBox>(content, "ExportRangeFilter");
                    var bookmarks = Find<CheckBox>(content, "ExportBookmarkedFilter");
                    var accept = Find<Button>(content, "AcceptDocumentPages");
                    SupplyThumbnails(dialog, renderer);
                    Require(list.Items.Count == 60 && dialog.SelectedPages.Count == 60, "Initial export must show only the current section.");
                    CaptureBoth("section");
                    section.SelectedIndex = 1;
                    Require(list.Items.Count == 1 && dialog.SelectedPages.Single().Id == document.Pages[1].Id, "Current-page filter lost its target.");
                    section.SelectedIndex = 0;
                    Require(list.Items.Count == 120, "Whole-notebook filter lost pages.");
                    CaptureBoth("all");
                    section.SelectedIndex = 3; search.Text = "LECTURE"; range.Text = "98–100";
                    Click(content, "ApplyExportRange");
                    Require(list.Items.Count == 3 && dialog.SelectedPages.Select(page => page.Id).SequenceEqual(document.Pages.Skip(97).Take(3).Select(page => page.Id)),
                        "Combined filters must preserve notebook order.");
                    CaptureBoth("filtered");
                    Click(content, "ClearDocumentPages");
                    Require(dialog.SelectedPages.Count == 0 && !accept.IsEnabled, "Clearing filtered pages must disable export.");
                    Click(content, "SelectAllDocumentPages");
                    Require(dialog.SelectedPages.Count == 3, "Select all must include only the three filtered pages.");
                    bookmarks.IsChecked = true;
                    Require(dialog.SelectedPages.Count == 1 && dialog.SelectedPages[0].Id == document.Pages[99].Id, "Bookmark filter must intersect with the other filters.");
                    bookmarks.IsChecked = false;
                    search.Text = "no matching synthetic page";
                    Require(list.Items.Count == 0 && !accept.IsEnabled, "An empty filter must not permit hidden pages to export.");
                    CaptureBoth("empty");
                    search.Text = ""; range.Text = "999"; Click(content, "ApplyExportRange");
                    Require(list.Items.Count == 0 && !accept.IsEnabled, "Invalid range must prevent export.");
                    CaptureBoth("invalid");
                    Click(content, "ResetExportFilters");
                    Require(list.Items.Count == 120 && dialog.SelectedPages.Count == 120 && accept.IsEnabled, "Reset must restore the full result list.");
                    CaptureBoth("reset");

                    void CaptureBoth(string scene)
                    {
                        foreach (var (width, height) in new[] { (856, 680), (616, 520) })
                            reports.Add(Capture(output, $"pdf-export-{locale}-{scene}", content, width, height, list.Items.Count, dialog.SelectedPages.Count));
                    }
                }
                finally { dialog.Close(); }
            }
            Require(original == JsonSerializer.Serialize(document, DocumentJson.Options), "Export filtering changed source pages.");
        }
        finally { L.SetLanguage(language); }
        File.WriteAllText(Path.Combine(output, "pdf-export-selection-preview.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static int RunInteractive(ResourceDictionary resources, bool compact, bool traditionalChinese)
    {
        L.SetLanguage(traditionalChinese ? "zh-TW" : "en");
        var application = new Application { Resources = resources, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dialog = CreateDialog(CreateNotebook(), Renderer());
        dialog.Title = "Penroam · Synthetic PDF Export Test (memory only)";
        dialog.ShowInTaskbar = true;
        if (compact) { dialog.Width = dialog.MinWidth; dialog.Height = dialog.MinHeight; }
        dialog.ShowDialog();
        application.Shutdown();
        return 0;
    }

    private static DocumentSelectionDialog CreateDialog(NotebookDocument document, PagePreviewRenderer renderer) =>
        new(null!, L.Text("Shell.ExportPdfPages"), document.Pages, renderer, document, document.Pages[1].Id, document.Sections[0].Id, false);

    private static PagePreviewRenderer Renderer() => new(new PreviewPdf(), _ => throw new InvalidOperationException("Synthetic export pages have no external assets."));

    private static NotebookDocument CreateNotebook()
    {
        var first = new NoteSection { Title = "Calculus · 微積分" };
        var second = new NoteSection { Title = "Revision and worked examples for the final examination · 期末複習與例題" };
        return new NotebookDocument
        {
            Title = "Synthetic export notebook", Sections = [first, second],
            Pages = Enumerable.Range(1, 120).Select(number => new NotePage
            {
                SectionId = number <= 60 ? first.Id : second.Id,
                Title = $"Lecture {number:D3} · Worked examples and revision notes · 課堂例題與複習筆記",
                IsBookmarked = number % 10 == 0, Template = PaperTemplate.Ruled,
                Texts = [new() { X = 50, Y = 60, Width = 650, Height = 300, FontSize = 24,
                    Text = $"Lecture {number:D3}\n\nSynthetic preview page.\n\nFilter this notebook by section, title, bookmark or page range." }]
            }).ToList()
        };
    }

    private static void SupplyThumbnails(DocumentSelectionDialog dialog, PagePreviewRenderer renderer)
    {
        // Detached controls do not get native Loaded/visibility notifications.
        var choices = (System.Collections.IEnumerable)typeof(DocumentSelectionDialog).GetField("_choices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        foreach (var choice in choices)
        {
            var type = choice.GetType();
            var page = (NotePage)type.GetProperty("Page")!.GetValue(choice)!;
            type.GetProperty("Thumbnail")!.SetValue(choice, renderer.RenderAsync(page, 64).GetAwaiter().GetResult());
        }
    }

    private static object Capture(string output, string scene, FrameworkElement content, int width, int height, int visiblePages, int selectedPages)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        Require(PresentationSource.FromVisual(content) is null, "Export content must remain detached.");
        var controls = new List<object>();
        var measured = new List<(string Id, Rect Bounds)>();
        foreach (var id in ControlIds)
        {
            var control = Find<Control>(content, id);
            var label = AutomationProperties.GetName(control);
            if (label.Length == 0 && control is ContentControl { Content: string text }) label = text;
            Require(label.Length > 0 && control.IsTabStop && control.Focusable, $"{scene}: {id} needs an accessible name and keyboard focus.");
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            Require(bounds.Width >= 43.5 && bounds.Height >= 43.5, $"{scene}: {id} is smaller than 44 DIP.");
            Require(bounds.Left >= -.5 && bounds.Top >= -.5 && bounds.Right <= width + .5 && bounds.Bottom <= height + .5,
                $"{scene}: {id} is outside the dialog.");
            foreach (var previous in measured)
            {
                var overlap = Rect.Intersect(bounds, previous.Bounds);
                Require(overlap.IsEmpty || overlap.Width <= .5 || overlap.Height <= .5, $"{scene}: {id} overlaps {previous.Id}.");
            }
            measured.Add((id, bounds));
            controls.Add(new { Id = id, Label = label, bounds.X, bounds.Y, bounds.Width, bounds.Height, control.IsEnabled });
        }
        var list = Find<ListBox>(content, "DocumentPageList");
        Require(list.ActualHeight >= 96, $"{scene}: the page list must retain room for at least one full page row.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var fileName = $"ui-preview-{scene}-{width}.png";
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, fileName))) encoder.Save(file);
        return new { Scene = scene, Width = width, Height = height, VisiblePages = visiblePages, SelectedPages = selectedPages, Controls = controls, Image = fileName };
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Click(DependencyObject content, string id) => Find<Button>(content, id).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static T Find<T>(DependencyObject content, string id) where T : DependencyObject =>
        Logical<T>(content).Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static IEnumerable<T> Logical<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { if (child is T result) yield return result; foreach (var nested in Logical<T>(child)) yield return nested; }
    }

    private sealed class PreviewPdf : IPdfService
    {
        public Task<IReadOnlyList<NotePage>> ImportAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string path, NotebookDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BitmapSource> RenderAsync(NotePage page, double scale, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paper = new PaperVisual { Width = page.Width, Height = page.Height, Template = page.Template };
            paper.Measure(new Size(page.Width, page.Height)); paper.Arrange(new Rect(0, 0, page.Width, page.Height));
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(page.Width * scale)), Math.Max(1, (int)Math.Ceiling(page.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(paper); bitmap.Freeze(); return Task.FromResult<BitmapSource>(bitmap);
        }
    }
}
