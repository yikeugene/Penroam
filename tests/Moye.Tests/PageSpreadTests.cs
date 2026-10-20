using System.Windows;
using Moye.Models;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class PageSpreadTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(27)]
    public void TwoPageRowsPairSectionOrderWithoutAddingOrCopyingPages(int count)
    {
        var pages = Enumerable.Range(1, count).Select(number => Page(number)).ToArray();
        var rows = PageSpreadViewModel.CreateRows(pages, true);
        Assert.Equal((count + 1) / 2, rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            Assert.Same(pages[index * 2], rows[index].First);
            Assert.Same(index * 2 + 1 < count ? pages[index * 2 + 1] : null, rows[index].Second);
            Assert.Equal(index * 2 + 1 < count, rows[index].HasSecond);
            Assert.True(rows[index].Contains(pages[index * 2]));
            Assert.False(rows[index].Contains(null));
        }
        Assert.Equal(pages, rows.SelectMany(row => row.Second is { } second ? new[] { row.First, second } : [row.First]));
    }

    [Fact]
    public void SinglePageRowsRetainEachOriginalPageAndItsNumber()
    {
        var pages = new[] { Page(1), Page(2), Page(3) };
        var rows = PageSpreadViewModel.CreateRows(pages, false);
        Assert.Equal(3, rows.Count);
        for (var index = 0; index < pages.Length; index++)
        {
            Assert.Same(pages[index], rows[index].First);
            Assert.Null(rows[index].Second); Assert.False(rows[index].HasSecond);
            Assert.Equal(index + 1, rows[index].First.Number);
        }
    }

    [Theory]
    [InlineData(.25)]
    [InlineData(.5)]
    [InlineData(1)]
    [InlineData(4)]
    public void UnequalPagesUseActualCanvasSizesWithTopAlignmentAndAnUnscaledGutter(double zoom)
    {
        var left = Page(1, 900, 450);
        var right = Page(2, 500, 1200);
        var spread = new PageSpreadViewModel(left, right);
        var size = PageSpreadGeometry.Measure(spread, zoom);
        var first = PageSpreadGeometry.GetPageBounds(spread, left, zoom);
        var second = PageSpreadGeometry.GetPageBounds(spread, right, zoom);
        Assert.Equal(1400 * zoom + 24, size.Width);
        Assert.Equal(1200 * zoom, size.Height);
        Assert.Equal(0, first.Top); Assert.Equal(0, second.Top);
        Assert.Equal(24, second.Left - first.Right);
        Assert.Equal(900 * zoom, first.Width); Assert.Equal(500 * zoom, second.Width);
        Assert.Equal(450 * zoom, first.Height); Assert.Equal(1200 * zoom, second.Height);
        Assert.Equal(size.Width, second.Right);
    }

    [Fact]
    public void OddTailHasNeitherAPhantomPageNorAnExtraGutter()
    {
        var rows = PageSpreadViewModel.CreateRows([Page(1), Page(2), Page(3, 460, 720)], true);
        Assert.Equal(new Size(230, 360), PageSpreadGeometry.Measure(rows[1], .5));
        Assert.Equal(new Rect(0, 0, 230, 360), PageSpreadGeometry.GetPageBounds(rows[1], rows[1].First, .5));
        Assert.Equal(2, PageSpreadGeometry.FitWidthZoom(rows[1], 920));
    }

    [Fact]
    public void FitWidthSubtractsOnlyTheFixedGutterAndFitPageUsesTheTallerCanvas()
    {
        var spread = new PageSpreadViewModel(Page(1, 1000, 600), Page(2, 600, 1200));
        var widthZoom = PageSpreadGeometry.FitWidthZoom(spread, 824);
        Assert.Equal(.5, widthZoom);
        Assert.Equal(824, PageSpreadGeometry.Measure(spread, widthZoom).Width);
        Assert.Equal(.4, PageSpreadGeometry.FitPageZoom(spread, 824, 480));
        Assert.Equal(.5, PageSpreadGeometry.FitPageZoom(spread, 824, 900));
    }

    [Theory]
    [InlineData(10, 10, .25)]
    [InlineData(50000, 50000, 4)]
    public void FitPreservesTheExistingZoomLimits(double width, double height, double expected)
    {
        var spread = new PageSpreadViewModel(Page(1), Page(2));
        Assert.Equal(expected, PageSpreadGeometry.FitWidthZoom(spread, width));
        Assert.Equal(expected, PageSpreadGeometry.FitPageZoom(spread, width, height));
    }

    [Fact]
    public void ProjectionAndMeasurementDoNotChangePersistedGeometryOrInkSnapshots()
    {
        var left = Page(1); var right = Page(2);
        left.Page.InkData = [1, 2, 3]; right.Page.InkData = [4, 5, 6];
        var leftInk = left.Page.InkData; var rightInk = right.Page.InkData;
        var leftId = left.Page.Id; var rightId = right.Page.Id;
        var spread = Assert.Single(PageSpreadViewModel.CreateRows([left, right], true));
        _ = PageSpreadGeometry.Measure(spread, 2);
        _ = PageSpreadGeometry.FitPageZoom(spread, 800, 600);
        Assert.Equal(leftId, spread.First.Page.Id); Assert.Equal(rightId, spread.Second!.Page.Id);
        Assert.Same(leftInk, left.Page.InkData); Assert.Same(rightInk, right.Page.InkData);
        Assert.Equal(800, left.Page.Width); Assert.Equal(1100, right.Page.Height);
        Assert.Throws<ArgumentException>(() => PageSpreadGeometry.GetPageBounds(spread, Page(3), 1));
    }

    private static PageViewModel Page(int number, double width = 800, double height = 1100) =>
        new(new NotePage { SectionId = "fixture", Width = width, Height = height }, number, 1);
}
