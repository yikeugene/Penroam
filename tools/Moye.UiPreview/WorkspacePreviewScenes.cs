using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;

namespace Moye.UiPreview;

internal static class WorkspacePreviewScenes
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Categories = ["Notebook", "Pages", "Insert & export", "Search", "Study", "View", "Tools", "Backup", "Help"];

    internal static IReadOnlyList<WorkspaceCommand> SnapshotCommands(MainWindow owner)
    {
        var ready = typeof(MainWindow).GetField("_ready", Fields)!;
        var wasReady = ready.GetValue(owner);
        try
        {
            // Loaded is deliberately not raised: no preference files, recovery
            // directories, update service or background saves are initialized.
            ready.SetValue(owner, true);
            var production = (IReadOnlyList<WorkspaceCommand>)typeof(MainWindow).GetMethod("CreateFeatureCommands", Fields)!.Invoke(owner, null)!;
            var snapshot = production.Select(command =>
            {
                var reason = command.UnavailableReason;
                var isChecked = command.IsChecked;
                return command with
                {
                    Execute = command.IsInstruction ? null : () => throw new InvalidOperationException("A detached catalog preview must not dispatch a command."),
                    GetUnavailableReason = () => reason,
                    GetChecked = command.GetChecked is null ? null : () => isChecked
                };
            }).ToArray();
            if (snapshot.Select(command => command.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
                snapshot.Any(command => string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Location)) ||
                !Categories.ToHashSet(StringComparer.Ordinal).SetEquals(snapshot.Select(command => command.Category)) ||
                snapshot.Any(command => command.ParentId is not null && !snapshot.Any(parent => parent.Id == command.ParentId)))
                throw new InvalidOperationException("The production catalog must preserve unique identities, locations, all nine categories and every parent/child relationship.");
            return snapshot;
        }
        finally { ready.SetValue(owner, wasReady); }
    }

    public static void Run(string output, MainWindow owner, IReadOnlyList<WorkspaceCommand> homeCommands)
    {
        var reports = new List<object>();
        var preferences = new WorkspacePreferences { TwoFingerNavigationOnly = true, LockZoom = true, BackupEnabled = true, BackupDirectory = @"C:\Example\Penroam Backups" };
        var original = JsonSerializer.Serialize(preferences);
        foreach (var (width, height) in new[] { (600, 670), (420, 500) })
        {
            var window = WorkspaceOptionsDialog.Build(owner, preferences); var content = DetachContent(window);
            foreach (var name in new[] { "TwoFingerNavigation", "LockZoom", "CompactToolbar", "FocusToolsDock", "BackupEnabled", "BackupFolder", "BackupInterval", "BackupRetention" })
                if (window.FindName(name) is not Control { Focusable: true })
                    throw new InvalidOperationException($"The workspace setting {name} must have a named focusable destination.");
            reports.Add(Capture(content, output, "workspace-settings", width, height));
            ((ScrollViewer)content).ScrollToBottom(); content.UpdateLayout();
            reports.Add(Capture(content, output, "workspace-settings-bottom", width, height)); VerifyDetached(window, content); window.Close();
        }
        if (JsonSerializer.Serialize(preferences) != original) throw new InvalidOperationException("Building or previewing settings changed preferences.");

        // The shell's detached page hosts do not receive Loaded. Register one
        // synthetic editor for the catalog's real selection/clipboard predicates.
        var editors = (Dictionary<Border, PageEditor>)typeof(MainWindow).GetField("_editors", Fields)!.GetValue(owner)!;
        var page = owner.ViewModel.SelectedPage?.Page ?? throw new InvalidOperationException("The editor catalog needs a selected synthetic page.");
        var host = new Border();
        editors.Add(host, new PageEditor(page, owner.ViewModel.Repository.GetAssetAsync));
        IReadOnlyList<WorkspaceCommand> editorCommands;
        try { editorCommands = SnapshotCommands(owner); }
        finally { editors.Remove(host); }
        if (editorCommands.Single(command => command.Id == "export-pdf").UnavailableReason is not null ||
            editorCommands.Single(command => command.Id == "paste").UnavailableReason is not null ||
            homeCommands.Single(command => command.Id == "export-pdf").UnavailableReason != "Open a notebook first.")
            throw new InvalidOperationException("Catalog previews must distinguish a ready editor from home, not a still-loading window.");
        if (!homeCommands.Select(command => command.Id).SequenceEqual(editorCommands.Select(command => command.Id)))
            throw new InvalidOperationException("Home must retain the complete catalog, including context-unavailable editor commands.");

        foreach (var (context, commands) in new[] { ("editor", editorCommands), ("home", homeCommands) })
        {
            reports.Add(new { Scene = "catalog-" + context, Commands = commands.Select(command => new
            {
                command.Id, command.ParentId, command.Name, command.Category, command.Location, command.Shortcut,
                command.Keywords, command.Detail, command.IsInstruction, command.UnavailableReason, command.IsChecked
            }).ToArray() });
            foreach (var (width, height) in new[] { (690, 640), (420, 400) })
                CaptureCatalog(owner, commands, context, output, width, height, reports);
        }
        File.WriteAllText(Path.Combine(output, "workspace-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CaptureCatalog(MainWindow owner, IReadOnlyList<WorkspaceCommand> commands, string context,
        string output, int width, int height, List<object> reports)
    {
        var window = WorkspaceCommandDialog.Build(owner, commands);
        var content = DetachContent(window);
        var search = (TextBox)window.FindName("CommandSearch");
        var category = (ComboBox)window.FindName("CommandCategory");
        var list = (ListBox)window.FindName("CommandList");
        var details = (TextBlock)window.FindName("CommandDetails");
        var empty = (TextBlock)window.FindName("CommandEmptyState");
        var count = (TextBlock)window.FindName("CommandResultCount");
        var reset = (Button)window.FindName("ClearCommandSearchButton");
        var run = (Button)window.FindName("RunCommandButton");
        var prefix = context == "editor" ? "workspace-commands" : "workspace-commands-home";
        WorkspaceCommand[] Expected() => commands.Where(command => command.Matches(search.Text) &&
                (category.SelectedIndex == 0 || command.Category == (string)category.SelectedItem))
            .OrderBy(command => command.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(command => command.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        void CheckResults()
        {
            if (list.Items.Count != Expected().Length || !count.Text.StartsWith(list.Items.Count + " ", StringComparison.Ordinal) ||
                (empty.Visibility == Visibility.Visible) != (list.Items.Count == 0))
                throw new InvalidOperationException("Catalog rows, result count and empty-state explanation must agree with the production metadata.");
        }
        void Capture(string suffix)
        {
            CheckResults();
            reports.Add(WorkspacePreviewScenes.Capture(content, output, prefix + suffix, width, height,
                "CommandSearch", "CommandCategory", "CommandList", "ClearCommandSearchButton", "CloseCommandButton", "CommandResultCount"));
            var detailsScroll = (ScrollViewer)details.Parent;
            if (detailsScroll.ViewportHeight <= 0 || detailsScroll.ScrollableWidth > .5 ||
                detailsScroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled ||
                detailsScroll.VerticalScrollBarVisibility != ScrollBarVisibility.Auto)
                throw new InvalidOperationException("Command explanations must wrap inside their own usable vertical scroll area.");
            VerifyDetached(window, content);
        }
        void Reset()
        {
            reset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (search.Text.Length != 0 || category.SelectedIndex != 0 || list.Items.Count != commands.Count || reset.IsEnabled)
                throw new InvalidOperationException("Reset must restore every parent, child and help topic across all categories.");
        }
        WorkspaceCommand Select(string id)
        {
            var expected = Expected(); var index = Array.FindIndex(expected, command => command.Id == id);
            if (index < 0) throw new InvalidOperationException($"The preview's current filter cannot reach {id}.");
            list.SelectedIndex = index;
            ScrollToCommand(content, list, index, width, height);
            var command = expected[index];
            if (!details.Text.Contains(command.Name, StringComparison.Ordinal) || !details.Text.Contains(command.Location, StringComparison.Ordinal) ||
                run.IsEnabled != command.CanExecute || (run.Visibility == Visibility.Collapsed) != command.IsInstruction ||
                command.UnavailableReason is { } reason && !details.Text.Contains(reason, StringComparison.Ordinal))
                throw new InvalidOperationException($"{command.Id}: details must show its real location, availability and action/instruction state.");
            return command;
        }

        if (window.Title != "All tools & help" || category.Items.Count != Categories.Length + 1 ||
            !Categories.ToHashSet(StringComparer.Ordinal).SetEquals(category.Items.Cast<string>().Skip(1)))
            throw new InvalidOperationException("All tools must expose every production category without requiring a search term.");
        Capture("");
        var reachable = VerifyEveryCommandReachable(content, list, Expected(), width, height);
        reports.Add(new { Scene = prefix + "-reachability", Width = width, Height = height, ReachableCommandIds = reachable });
        Capture("-bottom");
        category.SelectedItem = "Insert & export";
        Select("export-pdf");
        Capture(context == "home" ? "-disabled" : "-category");

        if (context == "editor")
        {
            category.SelectedIndex = 0; search.Text = "A4 overlap";
            if (!Expected().Any(command => command.Id == "a4-export") || Expected().Length >= commands.Count)
                throw new InvalidOperationException("Multiword catalog search must include child tools and match their explanatory text.");
            Select("a4-export"); Capture("-filtered");
            category.SelectedItem = "Tools"; search.Text = "no matching synthetic command 78246";
            Capture("-empty");
            if (list.Items.Count != 0 || run.IsEnabled || empty.Visibility != Visibility.Visible || !reset.IsEnabled)
                throw new InvalidOperationException("An empty catalog search must retain a visible recovery action and disable Run.");
            Reset(); Capture("-reset");
            category.SelectedItem = "Tools"; Select("text-color"); Capture("-disabled");
            if (run.IsEnabled || !details.Text.Contains("Select a text item first.", StringComparison.Ordinal))
                throw new InvalidOperationException("The selected-text requirement must remain discoverable without creating a text item.");
            category.SelectedItem = "Help"; Select("keyboard-help"); Capture("-help");
            Reset();
        }
        window.Close();
    }

    private static void ScrollToCommand(FrameworkElement content, ListBox list, int index, int width, int height)
    {
        // ScrollIntoView retains a bring-into-view anchor until WPF's queued
        // scroll/cache work completes. This detached renderer never pumps that
        // dispatcher lifecycle, so mixing it with direct offsets resets them.
        // Drive only the existing logical scroll client; keep the production
        // virtualization, containers, templates and viewport unchanged.
        Arrange(content, width, height);
        var panel = Descendants<VirtualizingStackPanel>(list).Single();
        var scroll = (IScrollInfo)panel;
        if (!VirtualizingPanel.GetIsVirtualizing(list) || VirtualizingPanel.GetScrollUnit(list) != ScrollUnit.Item ||
            !scroll.CanVerticallyScroll || scroll.ScrollOwner is not { CanContentScroll: true })
            throw new InvalidOperationException("Catalog reachability requires the production virtualized, item-scrolling list.");
        scroll.SetVerticalOffset(index);
        Arrange(content, width, height);
    }

    private static string[] VerifyEveryCommandReachable(FrameworkElement content, ListBox list, WorkspaceCommand[] commands, int width, int height)
    {
        var reached = new List<string>();
        foreach (var (command, index) in commands.Select((command, index) => (command, index)))
        {
            // Inspect the public item container and its rendered text, not the
            // dialog's private row wrapper or its implementation properties.
            list.SelectedIndex = index;
            ScrollToCommand(content, list, index, width, height);
            var row = list.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem
                ?? throw new InvalidOperationException($"{command.Id}: vertical scrolling did not realize the command row.");
            var title = Descendants<TextBlock>(row).FirstOrDefault(text => text.Text == command.Name)
                ?? throw new InvalidOperationException($"{command.Id}: its production title is missing from the rendered row.");
            Arrange(content, width, height);
            var viewport = Descendants<ScrollContentPresenter>(list).First();
            var viewportBounds = viewport.TransformToAncestor(content).TransformBounds(new Rect(viewport.RenderSize));
            var rowBounds = row.TransformToAncestor(content).TransformBounds(new Rect(row.RenderSize));
            var target = Rect.Intersect(rowBounds, viewportBounds);
            if (target.IsEmpty || target.Width < 43.99 || target.Height < 43.99 || !InsideViewport(title, content) ||
                !AutomationProperties.GetName(row).Contains(command.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"{command.Id}: the command title and a 44 DIP row target must be reachable by vertical scrolling at {width} × {height}. Row={rowBounds}, viewport={viewportBounds}, target={target}, titleInside={InsideViewport(title, content)}.");
            AssertHit(row, content, new Point(target.Left + target.Width / 2, target.Top + target.Height / 2), "catalog reachability", command.Name);
            reached.Add(command.Id);
        }
        var scroll = Descendants<ScrollViewer>(list).First();
        if (scroll.ScrollableHeight <= 0 || scroll.HorizontalOffset != 0 || scroll.ScrollableWidth > .5)
            throw new InvalidOperationException("The full production catalog must use vertical scrolling, not horizontal clipping or scrolling.");
        return reached.ToArray();
    }

    private static object Capture(FrameworkElement content, string output, string scene, int width, int height, params string[] requiredNames)
    {
        Arrange(content, width, height);
        var elements = Descendants<FrameworkElement>(content).ToArray();
        foreach (var name in requiredNames)
            if (!elements.Any(element => element.Name == name && element.ActualWidth > 0 && element.ActualHeight > 0 && Visible(element) && InsideViewport(element, content)))
                throw new InvalidOperationException($"{scene}: required control {name} is absent, collapsed or scroll-hidden.");
        var controls = elements.Where(element => element is Button or CheckBox or ListBoxItem or TextBox or ComboBox or MenuItem)
            .Where(element => element.ActualWidth > 0 && element.ActualHeight > 0 && Visible(element) && InsideViewport(element, content)).ToArray();
        var bounds = new List<(FrameworkElement Control, string Name, Rect Area)>();
        foreach (var control in controls)
        {
            var area = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            var name = AutomationProperties.GetName(control); if (string.IsNullOrEmpty(name)) name = (control as ContentControl)?.Content as string ?? control.GetType().Name;
            if (area.Width < 43.99 || area.Height < 43.99) throw new InvalidOperationException($"{scene}: {name} is smaller than 44 DIP: {area}.");
            if (area.Left < -.5 || area.Top < -.5 || area.Right > width + .5 || area.Bottom > height + .5) throw new InvalidOperationException($"{scene}: {name} is clipped: {area}.");
            AssertHit(control, content, new Point(area.Left + area.Width / 2, area.Top + area.Height / 2), scene, name);
            foreach (var previous in bounds.Where(previous => !previous.Control.IsAncestorOf(control) && !control.IsAncestorOf(previous.Control)))
            {
                var overlap = Rect.Intersect(previous.Area, area);
                if (!overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5)
                    throw new InvalidOperationException($"{scene}: {name} overlaps {previous.Name}: {overlap}.");
            }
            bounds.Add((control, name, area));
        }
        if (controls.Length == 0) throw new InvalidOperationException($"{scene}: no controls are visible.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var fileName = $"ui-preview-{scene}-{width}.png"; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, fileName))) encoder.Save(file);
        return new { Scene = scene, Width = width, Height = height, Image = fileName,
            Controls = bounds.Select(bound => new { bound.Name, bound.Area.X, bound.Area.Y, bound.Area.Width, bound.Area.Height }).ToArray() };
    }
    private static FrameworkElement DetachContent(Window window)
    {
        var content = (FrameworkElement)window.Content; window.Content = null;
        TextElement.SetFontFamily(content, window.FontFamily);
        TextElement.SetFontSize(content, window.FontSize);
        TextElement.SetForeground(content, window.Foreground);
        content.Language = window.Language;
        content.UseLayoutRounding = window.UseLayoutRounding;
        content.SnapsToDevicePixels = window.SnapsToDevicePixels;
        return content;
    }
    private static void Arrange(FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
    }
    private static void VerifyDetached(Window window, FrameworkElement content)
    {
        if (PresentationSource.FromVisual(content) is not null || new WindowInteropHelper(window).Handle != IntPtr.Zero)
            throw new InvalidOperationException("Workspace previews must not create a native window.");
    }
    private static void AssertHit(FrameworkElement control, FrameworkElement root, Point point, string scene, string name)
    {
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root, visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
            ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        while (hit is not null && hit != control) hit = VisualTreeHelper.GetParent(hit);
        if (hit != control) throw new InvalidOperationException($"{scene}: {name} does not receive its center pointer hit.");
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
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
