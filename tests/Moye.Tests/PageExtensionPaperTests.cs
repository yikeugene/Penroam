using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class PageExtensionPaperTests
{
    [Theory]
    [InlineData(PaperTemplate.Ruled, 60, 80)]
    [InlineData(PaperTemplate.Grid, 24, 60)]
    [InlineData(PaperTemplate.DotGrid, 24, 24)]
    [InlineData(PaperTemplate.Cornell, 78, 140)]
    [InlineData(PaperTemplate.Graph, 60, 79)]
    public async Task PaperGuidesKeepOriginalGeometryAndAlignmentAcrossEditorPreviewAndExport(PaperTemplate template, int markX, int markY)
    {
        using var directory = new StorageTestDirectory(); using var repository = new WorkflowRepository();
        var service = new PdfService(repository);
        var page = new NotePage { Width = 240, Height = 320, Template = template };
        var original = await service.RenderAsync(page, 1);
        var extended = PageExtensionService.Extend(page, new(48, 40, 80, 70));
        var background = await service.RenderAsync(extended, 1);
        var region = new byte[240 * 320 * 4]; background.CopyPixels(new Int32Rect(48, 40, 240, 320), region, 240 * 4, 0);
        var before = new byte[region.Length]; original.CopyPixels(before, 240 * 4, 0);
        Assert.Equal(before, region);
        Assert.Null(page.PaperLayout);
        Assert.Equal(new PaperPageLayout { X = 48, Y = 40, Width = 240, Height = 320 }, extended.PaperLayout);
        var editor = await Sta(() =>
        {
            var control = new PageEditor(extended, _ => throw new InvalidOperationException());
            control.Measure(new Size(extended.Width, extended.Height)); control.Arrange(new Rect(0, 0, extended.Width, extended.Height));
            var bitmap = new RenderTargetBitmap((int)extended.Width, (int)extended.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control); bitmap.Freeze(); return bitmap;
        });
        var output = Path.Combine(directory.Root, "extended-paper.pdf");
        await service.ExportAsync(output, new NotebookDocument { Pages = [extended] });
        var imported = Assert.Single(await service.ImportAsync(output));
        var exported = await service.RenderAsync(imported, 1);
        foreach (var bitmap in new BitmapSource[] { background, editor, exported })
        {
            Assert.True(HasBlueMarkNear(bitmap, markX + 48, markY + 40), $"Missing {template} guide at its translated position.");
            Assert.False(HasBlueMarkNear(bitmap, 20, 100));
            Assert.False(HasBlueMarkNear(bitmap, 320, 100));
            Assert.False(HasBlueMarkNear(bitmap, 100, 395));
        }
        // The cache must distinguish a fully patterned canvas of the same size from an extended paper region.
        var regular = extended.Snapshot(); regular.PaperLayout = null;
        Assert.NotSame(background, await service.RenderAsync(regular, 1));
        var again = PageExtensionService.Extend(extended, new(10, 20, 30, 40));
        Assert.Equal(new PaperPageLayout { X = 58, Y = 60, Width = 240, Height = 320 }, again.PaperLayout);
    }

    private static bool HasBlueMarkNear(BitmapSource bitmap, int x, int y)
    {
        var bytes = new byte[36]; bitmap.CopyPixels(new Int32Rect(x - 1, y - 1, 3, 3), bytes, 12, 0);
        for (var index = 0; index < bytes.Length; index += 4)
            if (bytes[index] > bytes[index + 2] + 3 && bytes[index + 2] < 249) return true;
        return false;
    }
    private static Task<T> Sta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception exception) { completion.SetException(exception); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
