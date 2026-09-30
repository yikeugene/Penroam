using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.UiPreview;

/// <summary>Detached synthetic document scenes; no window, user database or native input is used.</summary>
internal static class DocumentWorkflowPreviewScenes
{
    public static void Run(string output)
    {
        using var repository = new PreviewRepository();
        var pdf = new PreviewPdf(); var renderer = new PagePreviewRenderer(pdf, repository.GetAssetAsync);
        var document = repository.Document;
        var reports = new List<object>();
        foreach (var importing in new[] { true, false })
        {
            var dialog = new DocumentSelectionDialog(null!, importing ? "Import document pages" : "Export PDF pages", document.Pages,
                renderer, document, document.Pages[1].Id, document.Sections[0].Id, importing, "Lecture notes");
            var content = (FrameworkElement)dialog.Content; dialog.Content = null;
            VerifySelection(dialog, content, importing, document);
            // Supply bounded synthetic previews directly; detached controls never
            // receive native Loaded/visibility events, unlike the live dialog.
            var choices = (System.Collections.IEnumerable)typeof(DocumentSelectionDialog).GetField("_choices", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            foreach (var choice in choices)
            {
                var type = choice.GetType(); var page = (NotePage)type.GetProperty("Page")!.GetValue(choice)!;
                type.GetProperty("Thumbnail")!.SetValue(choice, renderer.RenderAsync(page, 64).GetAwaiter().GetResult());
            }
            var prefix = importing ? "document-import" : "document-export";
            foreach (var (width, height) in new[] { (900, 700), (700, 620) })
                reports.Add(Capture(output, prefix, content, width, height));
            var list = Descendants<ListBox>(content).Single();
            list.ScrollIntoView(list.Items[^1]); content.UpdateLayout();
            reports.Add(Capture(output, prefix + "-last-page", content, 700, 620));
            if (!Descendants<TextBlock>(list).Any(block => block.Text.StartsWith("Page 100", StringComparison.Ordinal)))
                throw new InvalidOperationException("The final document page is not reachable through the selection list.");
            dialog.Close();
        }
        using var reference = new ReferencePane(repository, pdf, () => document);
        reference.InitializeAsync().GetAwaiter().GetResult();
        reports.Add(Capture(output, "document-reference", reference, 420, 700));
        reports.Add(Capture(output, "document-reference-compact", reference, 300, 700));
        var overflowPage = document.Pages[0].Snapshot(); overflowPage.Texts[0].Height = 10;
        var overflowDocument = new NotebookDocument { Pages = [overflowPage] };
        var warning = new TextOverflowDialog(null!, overflowDocument, TextFlow.FindOverflow(overflowDocument.Pages));
        var warningContent = (FrameworkElement)warning.Content; warning.Content = null;
        reports.Add(Capture(output, "document-export-overflow", warningContent, 620, 430)); warning.Close();
        File.WriteAllText(Path.Combine(output, "document-workflow-preview.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifySelection(DocumentSelectionDialog dialog, FrameworkElement content, bool importing, NotebookDocument document)
    {
        var before = JsonSerializer.Serialize(document, DocumentJson.Options);
        if (!importing)
        {
            var scope = Logical<ComboBox>(content).Single(box => AutomationProperties.GetName(box) == "Export scope");
            if (dialog.SelectedPages.Count != 60) throw new InvalidOperationException("Export must initially select the current section only.");
            scope.SelectedIndex = 1;
            if (dialog.SelectedPages.Count != 1 || dialog.SelectedPages[0].Id != document.Pages[1].Id) throw new InvalidOperationException("Current-page export targets the wrong page.");
            scope.SelectedIndex = 2;
            if (dialog.SelectedPages.Count != 100) throw new InvalidOperationException("Whole-notebook export must include all sections.");
        }
        var range = Logical<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Page range");
        var apply = Logical<Button>(content).Single(button => Equals(button.Content, "Apply range"));
        range.Text = "100, 1–3"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!dialog.SelectedPages.Select(page => page.Id).SequenceEqual(new[] { document.Pages[0].Id, document.Pages[1].Id, document.Pages[2].Id, document.Pages[99].Id }))
            throw new InvalidOperationException("Page-range selection must retain displayed order.");
        range.Text = "101"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (dialog.SelectedPages.Count != 4) throw new InvalidOperationException("A rejected range changed the selection.");
        Logical<Button>(content).Single(button => Equals(button.Content, "Select all")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (before != JsonSerializer.Serialize(document, DocumentJson.Options)) throw new InvalidOperationException("Preview actions changed the source notebook.");
    }

    private static IEnumerable<T> Logical<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T result) yield return result;
            foreach (var nested in Logical<T>(child)) yield return nested;
        }
    }

    private static object Capture(string output, string name, FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var path = Path.Combine(output, $"ui-preview-{name}-{width}.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(path)) encoder.Save(file);
        var buttons = Descendants<ButtonBase>(content).Where(button => button is Button or CheckBox or RadioButton).Where(Visible).Where(button => InsideViewport(button, content)).ToArray();
        foreach (var button in buttons)
        {
            if (button.ActualWidth < 43.5 || button.ActualHeight < 43.5) throw new InvalidOperationException($"{name}: {button.Content} is smaller than 44 DIP.");
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            if (bounds.Right > width + .5 || bounds.Bottom > height + .5 || bounds.Left < -.5 || bounds.Top < -.5)
                throw new InvalidOperationException($"{name}: {button.Content} falls outside the layout.");
        }
        return new { Scene = name, Width = width, Height = height, Buttons = buttons.Length, Image = Path.GetFileName(path), Pages = 100 };
    }
    private static bool Visible(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
    private static bool InsideViewport(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        for (var current = VisualTreeHelper.GetParent(element); current is not null && current != root; current = VisualTreeHelper.GetParent(current))
            if (current is ScrollContentPresenter viewport)
            {
                var area = viewport.TransformToAncestor(root).TransformBounds(new Rect(viewport.RenderSize));
                if (!area.Contains(bounds)) return false;
            }
        return true;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T result) yield return result;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
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
    private sealed class PreviewRepository : INotebookRepository
    {
        public NotebookDocument Document { get; }
        public PreviewRepository()
        {
            var first = new NoteSection { Title = "Lectures" }; var second = new NoteSection { Title = "Revision" };
            Document = new() { Title = "One hundred lecture pages", Sections = [first, second], Pages = Enumerable.Range(1, 100).Select(index => new NotePage
            {
                SectionId = index <= 60 ? first.Id : second.Id, Template = PaperTemplate.Ruled,
                Texts = [new() { Text = $"Lecture {index}\n\nImportant concepts\n• Read the question\n• Show your work", Width = 640, Height = 300 }]
            }).ToList() };
        }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([new() { Id = Document.Id, Title = Document.Title, PageCount = 100 }]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(Document.Snapshot());
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes) => throw new NotSupportedException();
        public Task<AssetData> GetAssetAsync(string id) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
