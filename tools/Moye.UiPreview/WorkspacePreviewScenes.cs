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

namespace Moye.UiPreview;

internal static class WorkspacePreviewScenes
{
    public static void Run(string output)
    {
        var owner = new Window(); var reports = new List<object>();
        var preferences = new WorkspacePreferences { TwoFingerNavigationOnly = true, LockZoom = true, BackupEnabled = true, BackupDirectory = @"C:\Example\Moye Backups" };
        var original = JsonSerializer.Serialize(preferences);
        foreach (var (width, height) in new[] { (600, 670), (420, 500) })
        {
            var window = WorkspaceOptionsDialog.Build(owner, preferences); var content = (FrameworkElement)window.Content; window.Content = null;
            reports.Add(Capture(content, output, "workspace-settings", width, height));
            ((ScrollViewer)content).ScrollToBottom(); content.UpdateLayout();
            reports.Add(Capture(content, output, "workspace-settings-bottom", width, height)); window.Close();
        }
        var commands = new[]
        {
            new WorkspaceCommand("Quick note", "Start writing immediately in your Inbox, then organize the page later.", () => throw new InvalidOperationException("A detached preview must not run commands.")),
            new WorkspaceCommand("Find content · Ctrl+F", "Search page titles, typed notes and original PDF text without leaving this device.", () => throw new InvalidOperationException()),
            new WorkspaceCommand("Writing guard", "Use two fingers to move pages and lock zoom while you write.", () => throw new InvalidOperationException()),
            new WorkspaceCommand("Organize pages", "Select pages, drag to reorder, or move them to another section or notebook.", () => throw new InvalidOperationException()),
            new WorkspaceCommand("Backups and recovery", "Create editable backups, recover deleted pages, or inspect recent backup failures.", () => throw new InvalidOperationException())
        };
        foreach (var (width, height) in new[] { (690, 640), (420, 400) })
        {
            var window = WorkspaceCommandDialog.Build(owner, commands); var content = (FrameworkElement)window.Content; window.Content = null;
            reports.Add(Capture(content, output, "workspace-commands", width, height));
            var search = Descendants<TextBox>(content).Single(); search.Text = "PDF";
            if (Descendants<ListBox>(content).Single().Items.Count != 1) throw new InvalidOperationException("Command filtering did not match command details.");
            reports.Add(Capture(content, output, "workspace-commands-filtered", width, height)); window.Close();
        }
        if (JsonSerializer.Serialize(preferences) != original) throw new InvalidOperationException("Building or previewing settings changed preferences.");
        File.WriteAllText(Path.Combine(output, "workspace-preview-report.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })); owner.Close();
    }

    private static object Capture(FrameworkElement content, string output, string scene, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var controls = Descendants<FrameworkElement>(content).Where(element => element is Button or CheckBox or ListBoxItem or TextBox)
            .Where(element => element.ActualWidth > 0 && element.ActualHeight > 0 && Visible(element) && InsideViewport(element, content)).ToArray();
        var bounds = new List<object>();
        foreach (var control in controls)
        {
            var area = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            var name = AutomationProperties.GetName(control); if (string.IsNullOrEmpty(name)) name = (control as ContentControl)?.Content as string ?? control.GetType().Name;
            if (area.Width < 43.99 || area.Height < 43.99) throw new InvalidOperationException($"{scene}: {name} is smaller than 44 DIP: {area}.");
            if (area.Left < -.5 || area.Top < -.5 || area.Right > width + .5 || area.Bottom > height + .5) throw new InvalidOperationException($"{scene}: {name} is clipped: {area}.");
            AssertHit(control, content, new Point(area.Left + area.Width / 2, area.Top + area.Height / 2), scene, name);
            bounds.Add(new { Name = name, area.X, area.Y, area.Width, area.Height });
        }
        if (controls.Length == 0) throw new InvalidOperationException($"{scene}: no controls are visible.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var fileName = $"ui-preview-{scene}-{width}.png"; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, fileName))) encoder.Save(file);
        return new { Scene = scene, Width = width, Height = height, Image = fileName, Controls = bounds };
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
