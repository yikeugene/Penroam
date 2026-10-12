using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.UiPreview;

internal static class PdfPaginationPreviewScenes
{
    public static void Run(string output)
    {
        var page = PageExtensionService.Extend(new NotePage { Template = PaperTemplate.Ruled,
            Texts = [new() { X = 55, Y = 60, Width = 600, Height = 200, Text = "Lecture 04 · Differential equations\n\nRead the question, then show each step.", FontSize = 24 }] },
            new PageMargins(0, 0, 350, 240));
        page.Texts.Add(new() { X = 835, Y = 90, Width = 260, Height = 210, Text = "Extra notes\n\nKeep these readable when printing.", FontSize = 24, Color = "#FF3366AA" });
        var original = JsonSerializer.Serialize(page, DocumentJson.Options);
        var renderer = new PagePreviewRenderer(new PreviewPdf(), _ => throw new NotSupportedException());
        var dialog = new PdfExportLayoutDialog(null!, [page], renderer); var content = (FrameworkElement)dialog.Content; dialog.Content = null;
        var layout = Logical<ComboBox>(content).Single(item => AutomationProperties.GetName(item) == "Page layout");
        var orientation = Logical<ComboBox>(content).Single(item => AutomationProperties.GetName(item) == "A4 orientation");
        var overlap = Logical<TextBox>(content).Single(); var apply = Logical<Button>(content).Single(item => Equals(item.Content, "Continue to save"));
        var reports = new List<object>();
        if (dialog.OutputPageCount != 1 || !apply.IsEnabled) throw new InvalidOperationException("Original PDF export must keep one canvas per source page.");
        reports.Add(Capture(output, "pdf-layout-original", content, 640, 680));
        layout.SelectedIndex = 1;
        if (dialog.OutputPageCount != 4 || !apply.IsEnabled) throw new InvalidOperationException("Expanded canvas must split into four A4 sheets.");
        reports.Add(Capture(output, "pdf-layout-tiled", content, 640, 680));
        orientation.SelectedIndex = 2;
        reports.Add(Capture(output, "pdf-layout-compact", content, 540, 680));
        foreach (var invalid in new[] { "-1", "31", "NaN", "Infinity", "unknown" })
        { overlap.Text = invalid; if (apply.IsEnabled) throw new InvalidOperationException("Invalid overlap must disable export."); }
        reports.Add(Capture(output, "pdf-layout-invalid", content, 540, 680));
        overlap.Text = "0";
        if (!apply.IsEnabled || dialog.Options.OverlapMillimeters != 0) throw new InvalidOperationException("Correcting overlap must restore export.");
        layout.SelectedIndex = 0; overlap.Text = "unknown";
        if (!apply.IsEnabled || dialog.OutputPageCount != 1) throw new InvalidOperationException("Disabled A4 settings must not block original-size export.");
        if (original != JsonSerializer.Serialize(page, DocumentJson.Options)) throw new InvalidOperationException("Export layout preview must not change the notebook.");
        dialog.Close();
        File.WriteAllText(Path.Combine(output, "pdf-pagination-preview.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Capture(string output, string name, FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(content);
        var path = $"ui-preview-{name}-{width}.png"; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, path))) encoder.Save(file);
        foreach (var control in Logical<Control>(content).Where(item => item is Button or TextBox or ComboBox))
        {
            if (control.ActualHeight < 43.5 || control.ActualWidth < 43.5) throw new InvalidOperationException($"{name}: touch control is below 44 DIP.");
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5)
                throw new InvalidOperationException($"{name}: {AutomationProperties.GetName(control)} is outside the dialog.");
        }
        return new { Scene = name, Width = width, Height = height, Image = path };
    }
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
            cancellationToken.ThrowIfCancellationRequested(); var paper = new PaperVisual { Width = page.Width, Height = page.Height, Template = page.Template, PaperLayout = page.PaperLayout };
            paper.Measure(new Size(page.Width, page.Height)); paper.Arrange(new Rect(0, 0, page.Width, page.Height));
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(page.Width * scale)), Math.Max(1, (int)Math.Ceiling(page.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(paper); bitmap.Freeze(); return Task.FromResult<BitmapSource>(bitmap);
        }
    }
}
