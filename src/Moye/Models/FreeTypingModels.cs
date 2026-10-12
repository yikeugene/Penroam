namespace Moye.Models;

public sealed partial record NoteText
{
    // Legacy text remains an explicitly positioned box. New page typing uses a
    // borderless, content-sized run while retaining the same editable text data.
    public bool IsFreeFlow { get; set; }
    public double LineSpacing { get; set; }
}

public static partial class NoteTextLayout
{
    public static double LineHeight(NoteText text) =>
        double.IsFinite(text.LineSpacing) && text.LineSpacing > 0
            ? Math.Max(text.FontSize, text.LineSpacing) : text.FontSize * 1.4;
}