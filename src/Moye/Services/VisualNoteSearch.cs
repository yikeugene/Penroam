using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;
using Moye.Localization;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

public enum NoteColor { Any, Red, Orange, Yellow, Green, Blue, Purple, Black, Gray }
public enum NotePosition { Anywhere, TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }
public enum NoteMark { Any, Handwriting, Highlighter, TypedText }
public sealed record VisualNoteMatch(string PageId, string SectionId, string Title, Rect Region, int Count)
{
    private readonly string _title = Title;
    public LocalizedMessage? LocalizedTitle { get; init; }
    public string Title
    {
        get => LocalizedTitle?.ToString() ?? _title;
        init { _title = value; LocalizedTitle = null; }
    }
}

public static class VisualNoteSearch
{
    public static NoteColor Classify(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        if (max < 85) return NoteColor.Black;
        if (max - min < 30) return NoteColor.Gray;
        double hue = max == color.R ? 60d * (color.G - color.B) / (max - min)
            : max == color.G ? 120 + 60d * (color.B - color.R) / (max - min)
            : 240 + 60d * (color.R - color.G) / (max - min);
        if (hue < 0) hue += 360;
        return hue < 20 || hue >= 340 ? NoteColor.Red : hue < 45 ? NoteColor.Orange : hue < 75 ? NoteColor.Yellow
            : hue < 165 ? NoteColor.Green : hue < 265 ? NoteColor.Blue : NoteColor.Purple;
    }

    public static IReadOnlyList<VisualNoteMatch> Find(NotebookDocument book, NoteColor color, NotePosition position, NoteMark mark, string sectionId = "", CancellationToken token = default)
    {
        var results = new List<VisualNoteMatch>();
        var sectionNumbers = new Dictionary<string, int>();
        for (var index = 0; index < book.Pages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var page = book.Pages[index]; var pageNumber = sectionNumbers.GetValueOrDefault(page.SectionId) + 1; sectionNumbers[page.SectionId] = pageNumber; if (sectionId.Length > 0 && page.SectionId != sectionId) continue;
            var matched = Rect.Empty; var count = 0;
            bool Matches(Rect bounds, Color candidate)
            {
                if (color != NoteColor.Any && Classify(candidate) != color) return false;
                if (position == NotePosition.Anywhere) return true;
                var x = Math.Clamp((int)((bounds.X + bounds.Width / 2) / page.Width * 3), 0, 2);
                var y = Math.Clamp((int)((bounds.Y + bounds.Height / 2) / page.Height * 3), 0, 2);
                return (int)position == y * 3 + x + 1;
            }
            if (mark != NoteMark.TypedText && page.InkData.Length > 0)
            {
                var strokes = new StrokeCollection(new MemoryStream(page.InkData, false));
                foreach (var stroke in strokes)
                {
                    token.ThrowIfCancellationRequested();
                    if (mark == NoteMark.Highlighter && !stroke.DrawingAttributes.IsHighlighter ||
                        mark == NoteMark.Handwriting && stroke.DrawingAttributes.IsHighlighter) continue;
                    if (!Matches(stroke.GetBounds(), stroke.DrawingAttributes.Color)) continue;
                    matched.Union(stroke.GetBounds()); count++;
                }
            }
            if (mark is NoteMark.Any or NoteMark.TypedText)
                foreach (var text in page.Texts.Where(t => t.Text.Length > 0))
                {
                    var bounds = new Rect(text.X, text.Y, text.Width, text.Height);
                    if (!Matches(bounds, (Color)ColorConverter.ConvertFromString(text.Color))) continue;
                    matched.Union(bounds); count++;
                }
            if (count == 0) continue;
            var section = book.Sections.FirstOrDefault(s => s.Id == page.SectionId)?.Title ?? "General";
            results.Add(new(page.Id, page.SectionId, "", matched, count)
            { LocalizedTitle = L.M("Messages.Page.Location", section, pageNumber, page.Title.Length == 0 ? "" : $" · {page.Title}") });
        }
        return results;
    }
}
