using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Moye.Models;

namespace Moye.Controls;

/// <summary>Page-coordinate text measurement shared by continuation and export preflight.</summary>
public static class TextFlow
{
    public sealed record TextOverflow(string PageId, string TextId, int PageNumber);
    public sealed record Continuation(string RetainedText, NoteText Text);

    public static IReadOnlyList<TextOverflow> FindOverflow(IEnumerable<NotePage> pages)
    {
        var result = new List<TextOverflow>();
        var number = 0;
        foreach (var page in pages)
        {
            number++;
            foreach (var text in page.Texts)
                if (IsOverflowing(page, text)) result.Add(new(page.Id, text.Id, number));
        }
        return result;
    }

    public static bool IsOverflowing(NotePage page, NoteText text) =>
        RequiredHeight(text, text.Text) > Math.Min(text.Height, Math.Max(0, page.Height - text.Y)) + .5;

    public static double RequiredHeight(NoteText text, string content)
    {
        if (content.Length == 0 || content.EndsWith('\n') || content.EndsWith('\r')) content += " ";
        var formatted = new FormattedText(content, CultureInfo.GetCultureInfo("zh-HK"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(text.FontFamily), text.Italic ? FontStyles.Italic : FontStyles.Normal,
                text.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), text.FontSize, Brushes.Black, 1)
        {
            MaxTextWidth = NoteTextLayout.ContentWidth(text.Width),
            LineHeight = text.FontSize * 1.4,
            TextAlignment = text.Alignment switch { NoteTextAlignment.Center => TextAlignment.Center, NoteTextAlignment.Right => TextAlignment.Right, _ => TextAlignment.Left }
        };
        return Math.Ceiling(formatted.Height);
    }

    /// <summary>Does not mutate the source. Graphemes, list characters and line endings survive exactly.</summary>
    public static Continuation? CreateContinuation(NotePage page, NoteText text)
    {
        if (!IsOverflowing(page, text) || text.Text.Length == 0) return null;
        var boundaries = StringInfo.ParseCombiningCharacters(text.Text).Append(text.Text.Length).ToArray();
        var height = Math.Min(text.Height, Math.Max(0, page.Height - text.Y));
        int low = 0, high = boundaries.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (RequiredHeight(text, text.Text[..boundaries[middle]]) <= height + .5) low = middle;
            else high = middle - 1;
        }
        var split = boundaries[low];
        // Prefer a complete paragraph/list item, while keeping CRLF intact.
        var newline = split > 0 ? text.Text.LastIndexOf('\n', split - 1, split) : -1;
        if (newline >= 0 && newline + 1 <= split) split = newline + 1;
        if (split > 0 && split < text.Text.Length && text.Text[split - 1] == '\r' && text.Text[split] == '\n') split--;
        if (split >= text.Text.Length) return null;
        var x = Math.Min(text.X, Math.Max(0, page.Width - 60));
        var y = Math.Min(72, Math.Max(0, page.Height - 60));
        var next = text with
        {
            Id = Guid.NewGuid().ToString("N"), X = x, Y = y,
            Width = Math.Min(text.Width, page.Width - x),
            Height = Math.Max(1, page.Height - y), Text = text.Text[split..]
        };
        return new(text.Text[..split], next);
    }
}
