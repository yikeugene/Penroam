using System.Windows;

namespace Moye.ViewModels;

/// <summary>A presentation-only row; page identities and document order stay unchanged.</summary>
public sealed class PageSpreadViewModel(PageViewModel first, PageViewModel? second = null)
{
    public PageViewModel First { get; } = first;
    public PageViewModel? Second { get; } = second;
    public bool HasSecond => Second is not null;

    public bool Contains(PageViewModel? page) => ReferenceEquals(First, page) || Second is not null && ReferenceEquals(Second, page);

    public static IReadOnlyList<PageSpreadViewModel> CreateRows(IReadOnlyList<PageViewModel> pages, bool twoPageView)
    {
        var step = twoPageView ? 2 : 1;
        var rows = new List<PageSpreadViewModel>((pages.Count + step - 1) / step);
        for (var index = 0; index < pages.Count; index += step)
            rows.Add(new(pages[index], twoPageView && index + 1 < pages.Count ? pages[index + 1] : null));
        return rows;
    }
}

/// <summary>Paper geometry shared by the window, detached previews and tests. Chrome remains fixed DIP.</summary>
public static class PageSpreadGeometry
{
    public const double Gutter = 24;

    public static Size Measure(PageSpreadViewModel spread, double zoom) => new(
        (spread.First.Page.Width + (spread.Second?.Page.Width ?? 0)) * zoom + (spread.HasSecond ? Gutter : 0),
        Math.Max(spread.First.Page.Height, spread.Second?.Page.Height ?? 0) * zoom);

    public static Rect GetPageBounds(PageSpreadViewModel spread, PageViewModel page, double zoom)
    {
        if (!spread.Contains(page)) throw new ArgumentException("The page does not belong to this spread.", nameof(page));
        var left = ReferenceEquals(page, spread.First) ? 0 : spread.First.Page.Width * zoom + Gutter;
        return new(left, 0, page.Page.Width * zoom, page.Page.Height * zoom);
    }

    public static double FitWidthZoom(PageSpreadViewModel spread, double availableWidth) => Math.Clamp(
        (availableWidth - (spread.HasSecond ? Gutter : 0)) / (spread.First.Page.Width + (spread.Second?.Page.Width ?? 0)), .25, 4);

    public static double FitPageZoom(PageSpreadViewModel spread, double availableWidth, double availableHeight) => Math.Clamp(
        Math.Min(FitWidthZoom(spread, availableWidth), availableHeight / Math.Max(spread.First.Page.Height, spread.Second?.Page.Height ?? 0)), .25, 4);
}
