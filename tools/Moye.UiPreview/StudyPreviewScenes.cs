using System.IO;
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
using Moye.ViewModels;

namespace Moye.UiPreview;

/// <summary>Detached synthetic dialogs; no native window, input injection or user notes.</summary>
internal static class StudyPreviewScenes
{
    public static void Run(string output)
    {
        var owner = new Window(); using var repository = new StudyFixtureRepository(); using var model = new MainViewModel(repository);
        var section = new NoteSection { Title = "Mathematics" };
        var page = new NotePage { SectionId = section.Id, Title = "Substitution", Template = PaperTemplate.Ruled,
            Texts = [new() { X = 60, Y = 72, Text = "Example 3\nExplain why this substitution works.", FontFamily = "Segoe UI", FontSize = 22 }],
            StudyQuestions = [new() { Prompt = "Why does the substitution work?", Explanation = "Rewrite the inner function first, then check the derivative.",
                RetryNotes = "Try example 3 independently before looking at the worked answer.", Anchor = new() { X = 40, Y = 60, Width = 400, Height = 180 } },
                new() { Prompt = "How do I check the sign?", Status = StudyQuestionStatus.Resolved }],
            ResumeHint = new() { NextStep = "Try example 3 without the worked answer. Then check the sign in the second line.", Anchor = new() { X = 40, Y = 60, Width = 400, Height = 180 } } };
        model.ReplaceDocument(new NotebookDocument { Sections = [section], Pages = [page] }, true);
        var original = JsonSerializer.Serialize(page, DocumentJson.Options);
        var reports = new List<object>();
        foreach (var size in new[] { (640, 620), (480, 420) })
        {
            var dialog = new StudyQuestionDialog(owner, page.StudyQuestions[0]);
            reports.Add(Render(dialog, size.Item1, size.Item2, output, "study-question-" + size.Item1)); dialog.Close();
        }
        foreach (var size in new[] { (600, 420), (480, 360) })
        {
            var dialog = new StudyResumeDialog(owner, page.ResumeHint);
            reports.Add(Render(dialog, size.Item1, size.Item2, output, "study-resume-" + size.Item1)); dialog.Close();
        }
        foreach (var resume in new[] { false, true })
        foreach (var size in new[] { (760, 600), (480, 420) })
        {
            var dialog = new StudyInboxDialog(owner, model, resume); var content = (FrameworkElement)dialog.Content;
            var query = Logical<TextBox>(content).Single(); query.Text = resume ? "example" : "substitution";
            var results = Logical<ListBox>(content).Single();
            if (results.Items.Count != 1) throw new InvalidOperationException("Study inbox filtering lost the matching source.");
            query.Text = "";
            if (!resume)
            {
                var resolved = Logical<CheckBox>(content).Single(); resolved.IsChecked = true;
                if (results.Items.Count != 2) throw new InvalidOperationException("Include resolved did not reveal the completed question.");
            }
            results.SelectedIndex = 0;
            reports.Add(Render(dialog, size.Item1, size.Item2, output, (resume ? "study-resume-inbox-" : "study-inbox-") + size.Item1)); dialog.Close();
        }
        var editor = new PageEditor(page.Snapshot(), _ => throw new InvalidOperationException("The study fixture has no attachments."));
        using var bytes = new MemoryStream(editor.CreateThumbnail(500));
        var preview = new BitmapImage(); preview.BeginInit(); preview.CacheOption = BitmapCacheOption.OnLoad; preview.StreamSource = bytes; preview.EndInit(); preview.Freeze();
        foreach (var size in new[] { (680, 650), (480, 440) })
        {
            var dialog = new PageRegionDialog(owner, page, preview, "Mark the question source", new Rect(40, 60, 400, 180));
            if (dialog.Region != new Rect(40, 60, 400, 180)) throw new InvalidOperationException("Page region coordinates changed with the preview scale.");
            reports.Add(Render(dialog, size.Item1, size.Item2, output, "study-region-" + size.Item1)); dialog.Close();
        }
        if (JsonSerializer.Serialize(page, DocumentJson.Options) != original) throw new InvalidOperationException("Detached study dialogs mutated their source page.");
        File.WriteAllText(Path.Combine(output, "study-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        owner.Close();
    }

    private static object Render(Window window, int width, int height, string output, string name)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) throw new InvalidOperationException("Study preview created a native window.");
        var content = (FrameworkElement)window.Content; window.Content = null;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var controls = new List<object>();
        foreach (var button in Descendants<ButtonBase>(content).Where(button => button is Button or CheckBox))
        {
            if (!Visible(button) || button.ActualWidth <= 0 || button.ActualHeight <= 0 || !InsideViewport(button, content)) continue;
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            var label = AutomationProperties.GetName(button); if (label.Length == 0) label = button.Content as string ?? button.GetType().Name;
            if (bounds.Width < 43.99 || bounds.Height < 43.99) throw new InvalidOperationException($"{name}: {label} is smaller than 44 DIP.");
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5)
                throw new InvalidOperationException($"{name}: {label} extends outside the dialog.");
            controls.Add(new { Name = label, bounds.X, bounds.Y, bounds.Width, bounds.Height });
        }
        if (controls.Count == 0) throw new InvalidOperationException($"{name}: no usable controls are visible.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        Directory.CreateDirectory(output); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "ui-preview-" + name + ".png"))) encoder.Save(file);
        return new { Name = name, Width = width, Height = height, Controls = controls };
    }
    private static bool Visible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static bool InsideViewport(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        for (var current = VisualTreeHelper.GetParent(element); current is not null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not ScrollContentPresenter viewport) continue;
            var available = viewport.TransformToAncestor(root).TransformBounds(new Rect(viewport.RenderSize));
            if (!available.Contains(bounds)) return false;
        }
        return true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { if (child is T item) yield return item; foreach (var nested in Logical<T>(child)) yield return nested; }
    }
    private sealed class StudyFixtureRepository : INotebookRepository
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(null);
        public Task SaveAsync(NotebookDocument document) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task<AssetData> GetAssetAsync(string id) => throw new NotSupportedException();
        public Task<AssetData> PutAssetAsync(string name, string type, byte[] bytes) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
