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

/// <summary>Detached synthetic page-extension previews and staged-interaction checks.</summary>
internal static class PageExtensionPreviewScenes
{
    public static void Run(string output)
    {
        var page = new NotePage { Title = "Lecture handout", Pdf = new PdfPageSource { AssetId = "synthetic-pdf" }, Texts = [new() { Text = "Keep this note in place", X = 60, Y = 80 }] };
        var before = JsonSerializer.Serialize(page, DocumentJson.Options);
        var dialog = new ExtendPageDialog(page, MakePreview(page));
        var content = (FrameworkElement)dialog.Content; dialog.Content = null;
        var reports = new List<object>();
        var apply = Logical<Button>(content).Single(button => Equals(button.Content, "Extend page"));
        var right = Logical<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Right extension in millimeters");
        var left = Logical<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Left extension in millimeters");
        var reset = Logical<Button>(content).Single(button => Equals(button.Content, "Reset"));
        if (apply.IsEnabled) throw new InvalidOperationException("A page extension must add space before Apply is available.");
        reports.Add(Capture(output, "page-extension-initial", content, 640, 600));

        Logical<Button>(content).Single(button => Equals(button.Content, "Both +50 mm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!apply.IsEnabled || Math.Abs(dialog.Margins.Right - 50 * 96 / 25.4) > .01 || Math.Abs(dialog.Margins.Bottom - 50 * 96 / 25.4) > .01)
            throw new InvalidOperationException("Quick extension actions do not add the requested space.");
        left.Text = "25";
        reports.Add(Capture(output, "page-extension-margins", content, 640, 600));
        reports.Add(Capture(output, "page-extension-compact", content, 540, 570));

        var handle = Descendants<Thumb>(content).Single(thumb => AutomationProperties.GetName(thumb) == "Extend bottom right edge");
        var dragBefore = dialog.Margins;
        var leftBefore = Canvas.GetLeft(handle); var topBefore = Canvas.GetTop(handle);
        handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        handle.RaiseEvent(new DragDeltaEventArgs(15, 10) { RoutedEvent = Thumb.DragDeltaEvent });
        if (dialog.Margins.Right <= dragBefore.Right || dialog.Margins.Bottom <= dragBefore.Bottom ||
            Canvas.GetLeft(handle) <= leftBefore || Canvas.GetTop(handle) <= topBefore)
            throw new InvalidOperationException("Dragging a corner must update both margins and the visual preview immediately.");
        reports.Add(Capture(output, "page-extension-dragging", content, 540, 570));
        handle.RaiseEvent(new DragCompletedEventArgs(15, 10, true) { RoutedEvent = Thumb.DragCompletedEvent });
        if (dialog.Margins != dragBefore) throw new InvalidOperationException("Canceling a preview drag must restore its staged margins.");
        handle.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        handle.RaiseEvent(new DragDeltaEventArgs(12, 8) { RoutedEvent = Thumb.DragDeltaEvent });
        var dragged = dialog.Margins;
        handle.RaiseEvent(new DragCompletedEventArgs(12, 8, false) { RoutedEvent = Thumb.DragCompletedEvent });
        if (dialog.Margins != dragged) throw new InvalidOperationException("Completing a preview drag discarded its staged margins.");

        foreach (var invalid in new[] { "-1", "NaN", "Infinity", "not a number", "99999999999" })
        {
            right.Text = invalid;
            if (apply.IsEnabled) throw new InvalidOperationException($"Invalid margin {invalid} should disable Apply.");
        }
        reports.Add(Capture(output, "page-extension-invalid", content, 540, 570));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (dialog.Margins != default || apply.IsEnabled) throw new InvalidOperationException("Reset must discard staged extension margins.");
        if (before != JsonSerializer.Serialize(page, DocumentJson.Options)) throw new InvalidOperationException("Page-extension preview interactions changed the source document.");
        dialog.Close();
        File.WriteAllText(Path.Combine(output, "page-extension-preview.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static BitmapSource MakePreview(NotePage page)
    {
        var content = new Grid { Width = page.Width, Height = page.Height, Background = Brushes.White };
        var heading = new StackPanel { Margin = new Thickness(52, 70, 52, 40) };
        heading.Children.Add(new TextBlock { Text = "LECTURE 04", FontSize = 18, Foreground = Brushes.Gray });
        heading.Children.Add(new TextBlock { Text = "Differential equations", FontSize = 40, Margin = new Thickness(0, 14, 0, 24) });
        heading.Children.Add(new TextBlock { Text = "A differential equation describes how a quantity changes.\n\nConsider the initial value problem:\n\ny′ = 2x + 3,   y(0) = 1\n\nIntegrate both sides, then use the initial condition.", TextWrapping = TextWrapping.Wrap, FontSize = 23 });
        for (var i = 0; i < 6; i++) heading.Children.Add(new Border { Height = 1, Background = Brushes.LightGray, Margin = new Thickness(0, 42, 0, 0) });
        content.Children.Add(heading); content.Measure(new Size(page.Width, page.Height)); content.Arrange(new Rect(0, 0, page.Width, page.Height));
        var image = new RenderTargetBitmap(397, 562, 48, 48, PixelFormats.Pbgra32); image.Render(content); image.Freeze(); return image;
    }

    private static object Capture(string output, string name, FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(content);
        var nameOnDisk = $"ui-preview-{name}-{width}.png"; var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, nameOnDisk))) encoder.Save(file);
        var controls = Descendants<Control>(content).Where(control => control is Button or TextBox or Thumb).Where(Visible).ToArray();
        foreach (var control in controls)
        {
            if (control.ActualWidth < 43.5 || control.ActualHeight < 43.5) throw new InvalidOperationException($"{name}: {AutomationProperties.GetName(control)} is smaller than 44 DIP.");
            var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
            if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5)
                throw new InvalidOperationException($"{name}: {AutomationProperties.GetName(control)} is outside the dialog.");
        }
        return new { Scene = name, Width = width, Height = height, Controls = controls.Length, Image = nameOnDisk };
    }

    private static bool Visible(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static IEnumerable<T> Logical<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T result) yield return result;
            foreach (var nested in Logical<T>(child)) yield return nested;
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T result) yield return result;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
