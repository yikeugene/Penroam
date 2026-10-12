using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;

namespace Moye.UiPreview;

/// <summary>Detached, synthetic previews; no personal notebooks or native input.</summary>
internal static class FreeTypingPreviewScenes
{
    public static void Run(string output)
    {
        var reports = new List<object>();
        var plain = Editor(new NotePage { Width = 794, Height = 520 });
        plain.AddFreeTextAt(new Point(62, 56), "Ideas can start anywhere.\nType directly on the paper, then add a sketch.");
        plain.AddFreeTextAt(new Point(215, 230), "An independent thought\nwith room to grow.");
        AddInk(plain, [new(83, 268), new(83, 326), new(172, 326), new(172, 268), new(83, 268)], Colors.SteelBlue);
        plain.SetTool(InkTool.Text, Colors.Black, 3);
        reports.Add(Capture(output, "free-typing-plain", plain, expectChrome: false));

        var ruled = Editor(new NotePage { Width = 794, Height = 520, Template = PaperTemplate.Ruled });
        ruled.AddFreeTextAt(new Point(58, 80), "Every line follows the paper.\n繁體中文和 English 都可以自由輸入。\nWrapped text stays aligned with the horizontal rules.");
        ruled.SetTextDefaults(new NoteText { FontFamily = "Segoe UI", FontSize = 32, Color = "#FF315E79" });
        ruled.AddFreeTextAt(new Point(58, 274), "Larger text uses more space.\nThe following line still aligns.");
        reports.Add(Capture(output, "free-typing-ruled", ruled, expectChrome: false));
        ruled.SetTool(InkTool.Select, Colors.Black, 3);
        reports.Add(Capture(output, "free-typing-selection", ruled, expectChrome: true));

        var meetingPage = new NotePage { Template = PaperTemplate.Meeting };
        var meeting = Editor(meetingPage);
        meeting.SetTextDefaults(new NoteText { FontFamily = "Segoe UI", FontSize = 21, Color = "#FF25334A" });
        meeting.AddFreeTextAt(new Point(58, 104), "Weekly product review");
        meeting.AddFreeTextAt(new Point(58, 160), "12 Oct 2026, 10:00–10:30");
        meeting.AddFreeTextAt(new Point(58, 216), "Alex, Sam, Jordan");
        meeting.AddFreeTextAt(new Point(58, 272), "1. Review customer feedback\n2. Agree on the next experiment");
        meeting.AddFreeTextAt(new Point(58, 360), "Users want to continue their train of thought.\nKeep a short reminder next to the unfinished work.\nSketches and typed notes share the same page.");
        AddInk(meeting, [new(68, 520), new(85, 540), new(119, 493)], Colors.SeaGreen);
        AddInk(meeting, [new(139, 520), new(368, 520)], Colors.SeaGreen);
        meeting.AddFreeTextAt(new Point(58, meetingPage.Height - 296), "Try the resume hint with the next study session.");
        meeting.AddFreeTextAt(new Point(58, meetingPage.Height - 168), "Gather feedback  /  Sam  /  Friday\nReview the result  /  Alex  /  Monday");
        reports.Add(Capture(output, "free-typing-meeting", meeting, expectChrome: false));
        File.WriteAllText(Path.Combine(output, "free-typing-preview.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static PageEditor Editor(NotePage page) => new(page, _ => throw new InvalidOperationException("Synthetic fixture has no attachments."));
    private static void AddInk(PageEditor editor, Point[] points, Color color)
    {
        var stroke = new Stroke(new StylusPointCollection(points.Select(point => new StylusPoint(point.X, point.Y))))
        { DrawingAttributes = new DrawingAttributes { Color = color, Width = 3, Height = 3, FitToCurve = false } };
        editor.InkCanvas.Strokes.Add(stroke); editor.CommitPendingEdits();
    }

    private static object Capture(string output, string name, PageEditor editor, bool expectChrome)
    {
        editor.Measure(new Size(editor.Width, editor.Height));
        editor.Arrange(new Rect(0, 0, editor.Width, editor.Height)); editor.UpdateLayout();
        var visibleHandles = Descendants<Thumb>(editor).Count(thumb => thumb.Visibility == Visibility.Visible);
        if (visibleHandles != (expectChrome ? 2 : 0)) throw new InvalidOperationException($"{name}: incorrect text selection chrome.");
        foreach (var text in editor.Page.Texts)
        {
            if (!text.IsFreeFlow || TextFlow.IsOverflowing(editor.Page, text)) throw new InvalidOperationException($"{name}: text does not fit.");
            if (text.X < 0 || text.Y < 0 || text.X + text.Width > editor.Width + .01 || text.Y + text.Height > editor.Height + .01)
                throw new InvalidOperationException($"{name}: text is outside the page.");
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.Width), (int)Math.Ceiling(editor.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(editor);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var fileName = $"ui-preview-{name}.png";
        using (var stream = File.Create(Path.Combine(output, fileName))) encoder.Save(stream);
        return new { Scene = name, Width = editor.Width, Height = editor.Height, TextRuns = editor.Page.Texts.Count, VisibleHandles = visibleHandles, Image = fileName };
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}