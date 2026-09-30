using System.IO;
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
using Moye.ViewModels;

namespace Moye.UiPreview;

/// <summary>Detached layout only. No native window, desktop input, real notes, files or network are read.</summary>
internal static class OrganizationPreviewScenes
{
    public static void Run(string output)
    {
        var owner = new Window(); using var repository = new OrganizationFixtureRepository();
        using var model = new MainViewModel(repository); model.InitializeAsync().GetAwaiter().GetResult();
        var document = repository.Document;
        var thumbnails = new Dictionary<string, BitmapSource>();
        foreach (var page in document.Pages)
        {
            var editor = new PageEditor(page.Snapshot(), _ => throw new InvalidOperationException("The fixture has no attachments."));
            using var stream = new MemoryStream(editor.CreateThumbnail(150));
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            thumbnails[page.Id] = image;
        }
        var reports = new List<object>();
        foreach (var size in new[] { (940, 720), (660, 500) })
        {
            var dialog = new PageManagerDialog(owner, document, thumbnails);
            reports.Add(Render(dialog, size.Item1, size.Item2, output, $"organization-pages-{size.Item1}"));
            dialog.Close();
        }
        foreach (var size in new[] { (760, 650), (460, 420) })
        {
            var dialog = new NotebookSearchDialog(owner, model);
            var surface = (FrameworkElement)dialog.Content;
            var results = Find<ListBox>(surface).Single();
            results.ItemsSource = new[]
            {
                new NotebookSearchResult(document.Id, "lectures", document.Pages[0].Id, "Linear Algebra", "Lectures", "3 · Eigenvectors", "An eigenvector keeps its direction when a linear transformation is applied."),
                new NotebookSearchResult(document.Id, "practice", document.Pages[1].Id, "Linear Algebra", "Exercises", "7 · Worked examples", "The eigenvalue tells us how the vector's length and direction change.")
            };
            reports.Add(Render(dialog, size.Item1, size.Item2, output, $"organization-search-{size.Item1}"));
            dialog.Close();
        }
        foreach (var width in new[] { 520, 420 })
        {
            var library = new LibraryOrganizationDialog(owner, model);
            reports.Add(Render(library, width, 355, output, $"organization-library-{width}")); library.Close();
            var appearance = new NotebookOrganizationDialog(owner, new NotebookSummary { Id = document.Id, Title = document.Title, IsPinned = true, CoverColor = "#536980" });
            reports.Add(Render(appearance, width, 380, output, $"organization-appearance-{width}")); appearance.Close();
            var page = new PageOrganizationDetailsDialog(owner, document.Pages[0]);
            reports.Add(Render(page, width, 380, output, $"organization-page-details-{width}")); page.Close();
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "organization-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        owner.Close();
    }

    private static object Render(Window dialog, int width, int height, string output, string name)
    {
        var content = (FrameworkElement)dialog.Content; dialog.Content = null;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var controls = new List<object>();
        foreach (var button in Find<ButtonBase>(content).Where(button => button is Button or CheckBox or RadioButton))
        {
            if (!Visible(button) || button.ActualWidth <= 0 || button.ActualHeight <= 0 || !InsideViewport(button, content)) continue;
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            var label = AutomationProperties.GetName(button); if (label.Length == 0) label = button.Content as string ?? button.GetType().Name;
            if (bounds.Width < 43.99 || bounds.Height < 43.99) throw new InvalidOperationException($"{name}: {label} is smaller than 44 DIP ({bounds.Width} × {bounds.Height}).");
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5)
                throw new InvalidOperationException($"{name}: {label} is clipped by the dialog bounds.");
            controls.Add(new { Name = label, bounds.X, bounds.Y, bounds.Width, bounds.Height });
        }
        if (controls.Count == 0) throw new InvalidOperationException($"{name}: no controls were visible.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        Directory.CreateDirectory(output); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, $"ui-preview-{name}.png"))) encoder.Save(file);
        return new { Name = name, Width = width, Height = height, Controls = controls };
    }

    private static bool Visible(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static bool InsideViewport(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        for (var current = VisualTreeHelper.GetParent(element); current is not null && current != root; current = VisualTreeHelper.GetParent(current))
        {
            if (current is not ScrollContentPresenter viewport) continue;
            var available = viewport.TransformToAncestor(root).TransformBounds(new Rect(viewport.RenderSize));
            if (bounds.Left < available.Left - .5 || bounds.Top < available.Top - .5 || bounds.Right > available.Right + .5 || bounds.Bottom > available.Bottom + .5) return false;
        }
        return true;
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Find<T>(child)) yield return nested; }
    }

    private sealed class OrganizationFixtureRepository : INotebookRepository
    {
        public NotebookDocument Document { get; } = new()
        {
            Id = "organization-preview", Title = "Linear Algebra", Folder = "This semester", IsPinned = true,
            Sections = [new() { Id = "lectures", Title = "Lectures" }, new() { Id = "practice", Title = "Exercises" }],
            Pages = Enumerable.Range(1, 8).Select(index => new NotePage { Id = $"page-{index}", SectionId = index <= 4 ? "lectures" : "practice",
                Title = index == 1 ? "Eigenvectors and transformations" : $"Lecture notes {index}", IsBookmarked = index is 1 or 5,
                Template = index % 2 == 0 ? PaperTemplate.Grid : PaperTemplate.Ruled,
                Texts = [new() { Text = $"Lecture {index}\n\nDefinitions and worked examples", X = 70, Y = 75, Width = 500 }] }).ToList()
        };
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => Task.FromResult<IReadOnlyList<NotebookSummary>>([new() { Id = Document.Id, Title = Document.Title, Folder = Document.Folder, IsPinned = true, PageCount = Document.Pages.Count }, new() { Id = "other", Title = "Design journal", Folder = "Projects" }]);
        public Task<NotebookDocument?> LoadAsync(string id) => Task.FromResult<NotebookDocument?>(id == Document.Id ? Document.Snapshot() : null);
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes) => throw new NotSupportedException();
        public Task<AssetData> GetAssetAsync(string id) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
