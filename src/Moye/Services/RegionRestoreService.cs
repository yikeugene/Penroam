using System.IO;
using System.Windows;
using System.Windows.Ink;
using Moye.Models;

namespace Moye.Services;

/// <summary>Restore complete objects in a region; crossing objects and later edits elsewhere survive.</summary>
public static class RegionRestoreService
{
    public static bool Compatible(NotePage current, NotePage previous) =>
        current.Id == previous.Id && current.Width == previous.Width && current.Height == previous.Height
        && Equals(current.Pdf, previous.Pdf) && Equals(current.PaperLayout, previous.PaperLayout)
        && current.Template == previous.Template;

    public static NotePage Restore(NotePage current, NotePage previous, Rect region)
    {
        if (!Compatible(current, previous)) throw new InvalidOperationException("Choose a version with the same page size and background.");
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0 || !new Rect(0, 0, current.Width, current.Height).Contains(region))
            throw new ArgumentException("Choose a region inside the page.");
        var result = current.Snapshot();
        result.Texts.RemoveAll(t => region.Contains(new Rect(t.X, t.Y, t.Width, t.Height)));
        var retainedText = result.Texts.Select(t => t.Id).ToHashSet();
        result.Texts.AddRange(previous.Texts.Where(t => !retainedText.Contains(t.Id) && region.Contains(new Rect(t.X, t.Y, t.Width, t.Height))).Select(t => t with {}));
        result.Images.RemoveAll(i => region.Contains(new Rect(i.X, i.Y, i.Width, i.Height)));
        var retainedImages = result.Images.Select(i => i.Id).ToHashSet();
        result.Images.AddRange(previous.Images.Where(i => !retainedImages.Contains(i.Id) && region.Contains(new Rect(i.X, i.Y, i.Width, i.Height))).Select(i => i with {}));
        var ink = Read(current.InkData);
        foreach (var stroke in ink.Where(s => region.Contains(s.GetBounds())).ToArray()) ink.Remove(stroke);
        foreach (var stroke in Read(previous.InkData).Where(s => region.Contains(s.GetBounds()))) ink.Add(stroke.Clone());
        using var output = new MemoryStream(); ink.Save(output); result.InkData = ink.Count == 0 ? [] : output.ToArray();
        return result;
    }

    private static StrokeCollection Read(byte[] bytes) => bytes.Length == 0 ? new() : new(new MemoryStream(bytes, false));
}
