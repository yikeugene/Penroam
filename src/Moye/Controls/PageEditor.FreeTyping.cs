using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Moye.Models;

namespace Moye.Controls;

public sealed partial class PageEditor
{
    public NoteText AddFreeTextAt(Point location, string text = "") => AddTextAt(location, text, freeFlow: true);
}

/// <summary>Placement in document coordinates shared by typing and continuation.</summary>
public static class FreeTypingLayout
{
    public static Point StartPoint(NotePage page)
    {
        var y = page.Template == PaperTemplate.Meeting ? 80d : 72d;
        if (page.Practice is { } practice)
            y = page.Images.Where(image => image.AssetId == practice.QuestionAssetId).Select(image => image.Y + image.Height + 32).DefaultIfEmpty(y).Max();
        return new(Math.Min(72, Math.Max(0, page.Width - 100)), Math.Min(y, Math.Max(0, page.Height - 60)));
    }

    public static void Place(NotePage page, NoteText text, Point location)
    {
        text.IsFreeFlow = true;
        text.X = Math.Clamp(location.X, 0, Math.Max(0, page.Width - 60));
        text.Y = Math.Clamp(location.Y, 0, Math.Max(0, page.Height - NoteTextLayout.LineHeight(text)));
        text.Width = Math.Max(1, page.Width - text.X - Math.Min(40, page.Width * .05));
        AlignToRules(page, text, location.Y);
        text.Height = Math.Min(Math.Max(1, page.Height - text.Y), NoteTextLayout.LineHeight(text));
    }

    public static void AlignToRules(NotePage page, NoteText text, double? targetBaseline = null)
    {
        if (!text.IsFreeFlow) return;
        text.LineSpacing = 0;
        if (page.Pdf is not null || page.Template is not (PaperTemplate.Ruled or PaperTemplate.Cornell or PaperTemplate.Meeting)) return;
        var paper = page.PaperLayout ?? new PaperPageLayout { Width = page.Width, Height = page.Height };
        // Extended margins stay free-form; only actual horizontal guides snap.
        if (text.X < paper.X || text.X > paper.X + paper.Width || text.Y < paper.Y || text.Y >= paper.Y + paper.Height) return;
        var rules = PaperPattern.Lines(page.Template, paper.Width, paper.Height)
            .Where(line => line.Thickness < 1 && Math.Abs(line.Start.Y - line.End.Y) < .001
                && text.X >= paper.X + line.Start.X && text.X < paper.X + line.End.X).ToArray();
        if (rules.Length == 0) return;
        var spacing = page.Template == PaperTemplate.Meeting ? PaperPattern.MeetingScale(paper.Height) * 32 : 32;
        text.LineSpacing = Math.Ceiling(text.FontSize * 1.4 / spacing) * spacing;
        var baseline = Baseline(text);
        var preferred = targetBaseline ?? text.Y + baseline;
        var rule = rules.MinBy(line => Math.Abs(paper.Y + line.Start.Y - preferred));
        text.Y = Math.Max(0, paper.Y + rule.Start.Y - baseline);
        text.Width = Math.Min(text.Width, Math.Max(1, paper.X + rule.End.X - text.X));
    }

    public static double Baseline(NoteText text) => new FormattedText("Ag國", CultureInfo.GetCultureInfo("zh-HK"), FlowDirection.LeftToRight,
        new Typeface(new FontFamily(text.FontFamily), text.Italic ? FontStyles.Italic : FontStyles.Normal,
            text.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), text.FontSize, Brushes.Black, 1)
        { LineHeight = NoteTextLayout.LineHeight(text) }.Baseline;
}