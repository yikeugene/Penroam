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
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;
using L = Moye.Localization.LocalizationService;

namespace Moye.UiPreview;

/// <summary>Read-only synthetic insight dialogs. Never shows a native window or accesses the user's notes.</summary>
internal static class InsightPreviewScenes
{
    public static void Run(string output)
    {
        var section = new NoteSection { Title = "Mathematics" };
        var page = new NotePage { SectionId = section.Id, Title = "Substitution and sign checks", Template = PaperTemplate.Ruled,
            Texts = [new() { X = 55, Y = 70, Width = 640, Height = 130, Text = "Worked example\nCheck each substitution carefully.", FontSize = 22, Color = "#FF2255BB" },
                new() { X = 55, Y = 870, Width = 220, Height = 120, Text = "Check the sign\nbefore simplifying.", FontSize = 22, Color = "#FFCC3333" }] };
        var other = new NotePage { SectionId = section.Id, Title = "Practice reminders", Template = PaperTemplate.Cornell,
            Texts = [new() { X = 560, Y = 70, Width = 150, Height = 100, Text = "Try again", FontSize = 22, Color = "#FFCC3333" }] };
        var book = new NotebookDocument { Sections = [section], Pages = [page, other] };
        var original = JsonSerializer.Serialize(book, DocumentJson.Options);
        var renderer = new PagePreviewRenderer(new PreviewPdf(), _ => throw new NotSupportedException());
        var reports = new List<object>();

        var matches = VisualNoteSearch.Find(book, NoteColor.Red, NotePosition.BottomLeft, NoteMark.TypedText);
        if (matches.Count != 1 || matches[0].PageId != page.Id || matches[0].Count != 1)
            throw new InvalidOperationException("Appearance fixture lost its red bottom-left typed note.");
        if (VisualNoteSearch.Find(book, NoteColor.Blue, NotePosition.Anywhere, NoteMark.TypedText).Count != 1 ||
            VisualNoteSearch.Find(book, NoteColor.Green, NotePosition.Anywhere, NoteMark.Any).Count != 0)
            throw new InvalidOperationException("Appearance fixture color filters disagree with its marks.");
        foreach (var (width, height) in new[] { (780, 620), (480, 540) })
        {
            var dialog = new VisualNoteSearchDialog(null!, book, renderer); var content = (FrameworkElement)dialog.Content;
            Logical<ComboBox>(content).Single(box => AutomationProperties.GetName(box) == "Color").SelectedItem = NoteColor.Red;
            Logical<ComboBox>(content).Single(box => AutomationProperties.GetName(box) == "Position on page").SelectedItem = NotePosition.BottomLeft;
            Logical<ComboBox>(content).Single(box => AutomationProperties.GetName(box) == "Mark").SelectedItem = NoteMark.TypedText;
            // Run the real asynchronous search on the detached dispatcher tree.
            CompleteDialogOperation(dialog, "SearchAsync");
            var list = Logical<ListBox>(content).Single();
            if (list.Items.Count != 1 || Field<TextBlock>(dialog, "_status").Text != L.Text("Controls.VisualSearch.ShowingCount", 1, 1))
                throw new InvalidOperationException("Appearance search lost its result count.");
            list.SelectedIndex = 0;
            if (dialog.Result?.PageId != page.Id || !Logical<Button>(content).Single(button => Equals(button.Content, "Open page")).IsEnabled)
                throw new InvalidOperationException("Appearance result selection cannot open its source.");
            reports.Add(Render(dialog, output, $"insight-appearance-{width}", width, height)); dialog.Close();
        }
        RunSearchBatches(output, reports);

        var group = Guid.NewGuid().ToString("N");
        var first = new NotePage { Practice = new() { GroupId = group, Number = 1, Question = "Explain why the substitution preserves the sign", Reflection = "I changed the sign too early." } };
        var second = new NotePage { Practice = new() { GroupId = group, Number = 2, Question = first.Practice.Question, Reflection = "Write the substitution explicitly first." } };
        foreach (var (width, height) in new[] { (650, 650), (450, 600) })
        {
            var dialog = new PracticeAttemptsDialog(null!, [first, second], first.Id); var content = (FrameworkElement)dialog.Content;
            var attempts = Logical<ListBox>(content).Single(); var reflection = Logical<TextBox>(content).Single(); var reviewed = Logical<CheckBox>(content).Single();
            reflection.Text = "Check the derivative before simplifying."; reviewed.IsChecked = true; attempts.SelectedIndex = 1;
            if (reflection.Text != second.Practice.Reflection) throw new InvalidOperationException("The second attempt did not load its own reflection.");
            reflection.Text = "The second attempt works. Explain the final sign."; attempts.SelectedIndex = 0;
            if (reflection.Text != "Check the derivative before simplifying." || reviewed.IsChecked != true ||
                dialog.Changes[second.Id].Reflection != "The second attempt works. Explain the final sign.")
                throw new InvalidOperationException("Switching attempts lost staged reflections or review state.");
            reports.Add(Render(dialog, output, $"insight-attempts-{width}", width, height)); dialog.Close();
        }
        if (first.Practice.Reflection != "I changed the sign too early." || first.Practice.Reviewed || second.Practice.Reflection != "Write the substitution explicitly first.")
            throw new InvalidOperationException("The attempts dialog changed its input before acceptance.");

        var earlier = page.Snapshot(); earlier.Texts[0].Text = "Earlier working\nThis is the region to recover.";
        foreach (var (width, height) in new[] { (660, 650), (450, 540) })
        {
            var dialog = new RegionHistoryDialog(null!, [page, earlier], renderer); var content = (FrameworkElement)dialog.Content;
            var versions = Logical<ComboBox>(content).Single(); versions.SelectedIndex = 1;
            if (!ReferenceEquals(dialog.Result, earlier) || !Logical<Button>(content).Single(button => Equals(button.Content, "Choose area…")).IsEnabled)
                throw new InvalidOperationException("Earlier-version preview did not enable region selection.");
            reports.Add(Render(dialog, output, $"insight-history-{width}", width, height)); dialog.Close();
        }
        var thumbnail = renderer.RenderAsync(page, 600).GetAwaiter().GetResult();
        foreach (var (width, height) in new[] { (680, 650), (470, 500) })
        {
            var initial = new Rect(40, 60, 400, 200); var dialog = new PageRegionDialog(null!, page, thumbnail, "Choose the area to restore", initial);
            var content = (FrameworkElement)dialog.Content; var fields = Logical<TextBox>(content).ToArray();
            fields.Single(field => AutomationProperties.GetName(field) == "Left in page units").Text = "-1";
            Logical<Button>(content).Single(button => Equals(button.Content, "Use region")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (dialog.Region != initial || !Logical<TextBlock>(content).Any(block => block.Text.Contains("inside the page")))
                throw new InvalidOperationException("Invalid numeric regions were not rejected without moving the selection.");
            Logical<Button>(content).Single(button => Equals(button.Content, "Whole page")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (dialog.Region != new Rect(0, 0, page.Width, page.Height)) throw new InvalidOperationException("Whole-page selection lost document coordinates.");
            reports.Add(Render(dialog, output, $"insight-region-{width}", width, height)); dialog.Close();
        }
        if (original != JsonSerializer.Serialize(book, DocumentJson.Options)) throw new InvalidOperationException("Insight dialogs mutated their source notebook.");
        File.WriteAllText(Path.Combine(output, "insight-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static int RunInteractive(ResourceDictionary resources, bool compact, bool traditionalChinese)
    {
        L.SetLanguage(traditionalChinese ? "zh-TW" : "en");
        var application = new Application { Resources = resources, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dialog = new VisualNoteSearchDialog(null!, SearchNotebook(), new PagePreviewRenderer(new PreviewPdf(), _ => throw new NotSupportedException()));
        dialog.Title = "Penroam · Synthetic Visual Search Test (memory only)";
        dialog.ShowInTaskbar = true;
        if (compact) { dialog.Width = 480; dialog.Height = 540; }
        dialog.ShowDialog();
        application.Shutdown();
        return 0;
    }

    private static NotebookDocument SearchNotebook()
    {
        var section = new NoteSection { Title = "Synthetic search · 搜尋測試" };
        return new NotebookDocument
        {
            Sections = [section], Pages = Enumerable.Range(1, 121).Select(number => new NotePage
            {
                Id = "visual-search-" + number, SectionId = section.Id, Title = $"Result {number:D3} · 搜尋結果", Width = 400, Height = 200,
                Texts = [new() { X = 30, Y = 30, Width = 320, Height = 120, Text = $"Synthetic note {number:D3}\n測試筆記 {number:D3}",
                    Color = number % 3 == 0 ? "#FF2255BB" : "#FFCC3333" }]
            }).ToList()
        };
    }

    private static void RunSearchBatches(string output, List<object> reports)
    {
        var language = L.Current.LanguageCode;
        var book = SearchNotebook();
        var original = JsonSerializer.Serialize(book, DocumentJson.Options);
        try
        {
            foreach (var locale in new[] { "en", "zh-TW" })
            foreach (var (width, height) in new[] { (780, 620), (480, 540) })
            {
                L.SetLanguage(locale);
                var dialog = new VisualNoteSearchDialog(null!, book, new PagePreviewRenderer(new PreviewPdf(), _ => throw new NotSupportedException()));
                try
                {
                    CompleteDialogOperation(dialog, "SearchAsync");
                    var list = Field<ListBox>(dialog, "_results");
                    var more = Field<Button>(dialog, "_loadMore");
                    if (list.Items.Count != 60 || !more.IsEnabled || !more.Focusable || AutomationProperties.GetName(more) != L.Text("Controls.VisualSearch.LoadMore"))
                        throw new InvalidOperationException("Appearance search must expose its remaining batches through an accessible button.");
                    reports.Add(Render(dialog, output, $"insight-appearance-{locale}-first-batch-{width}", width, height));
                    CompleteDialogOperation(dialog, "LoadMoreAsync");
                    if (list.Items.Count != 120 || !more.IsEnabled) throw new InvalidOperationException("The second appearance batch lost matching pages.");
                    CompleteDialogOperation(dialog, "LoadMoreAsync");
                    if (list.Items.Count != 121 || more.Visibility != Visibility.Collapsed ||
                        Field<TextBlock>(dialog, "_status").Text != L.Text("Controls.VisualSearch.ShowingCount", 121, 121))
                        throw new InvalidOperationException("Appearance search cannot reach every match.");
                    list.SelectedIndex = 120; list.ScrollIntoView(list.SelectedItem);
                    if (dialog.Result?.PageId != book.Pages[120].Id) throw new InvalidOperationException("The final search result cannot be selected.");
                    reports.Add(Render(dialog, output, $"insight-appearance-{locale}-last-batch-{width}", width, height));
                }
                finally { dialog.Close(); }
            }
            if (original != JsonSerializer.Serialize(book, DocumentJson.Options)) throw new InvalidOperationException("Appearance search mutated its source pages.");
        }
        finally { L.SetLanguage(language); }
    }

    private static void CompleteDialogOperation(VisualNoteSearchDialog dialog, string method)
    {
        var previous = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var task = (Task)typeof(VisualNoteSearchDialog).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null)!;
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static object Render(Window window, string output, string name, int width, int height)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) throw new InvalidOperationException("Insight preview created a native window.");
        var content = (FrameworkElement)window.Content; window.Content = null;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var controls = new List<object>();
        foreach (var control in Descendants<Control>(content).Where(control => control is Button or CheckBox or TextBox or ComboBox))
        {
            if (!Visible(control) || control.ActualWidth <= 0 || control.ActualHeight <= 0 || !InsideViewport(control, content)) continue;
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            var nameOfControl = AutomationProperties.GetName(control); if (nameOfControl.Length == 0) nameOfControl = (control as ContentControl)?.Content as string ?? control.GetType().Name;
            if (bounds.Width < 43.99 || bounds.Height < 43.99) throw new InvalidOperationException($"{name}: {nameOfControl} is smaller than 44 DIP.");
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5)
                throw new InvalidOperationException($"{name}: {nameOfControl} extends outside the dialog.");
            controls.Add(new { Name = nameOfControl, bounds.X, bounds.Y, bounds.Width, bounds.Height });
        }
        if (controls.Count == 0) throw new InvalidOperationException($"{name}: no controls are visible.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "ui-preview-" + name + ".png"))) encoder.Save(file);
        window.Content = content;
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
            if (current is ScrollContentPresenter viewport && !viewport.TransformToAncestor(root).TransformBounds(new Rect(viewport.RenderSize)).Contains(bounds)) return false;
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
    private sealed class PreviewPdf : IPdfService
    {
        public Task<IReadOnlyList<NotePage>> ImportAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExportAsync(string path, NotebookDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BitmapSource> RenderAsync(NotePage page, double scale, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) { drawing.PushTransform(new ScaleTransform(scale, scale)); PaperPattern.Draw(drawing, page.Template, page.Width, page.Height, page.PaperLayout); drawing.Pop(); }
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(page.Width * scale)), Math.Max(1, (int)Math.Ceiling(page.Height * scale)), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return Task.FromResult<BitmapSource>(bitmap);
        }
    }
}
