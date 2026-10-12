using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class FreeTypingTests
{
    [Fact]
    public void FreeTypingHasNoFrameAndSelectionStillProvidesMoveAndResize()
    {
        Sta(() =>
        {
            var editor = Editor(new NotePage());
            var text = editor.AddFreeTextAt(new Point(90, 120), "Meeting notes");
            Layout(editor);
            Assert.True(text.IsFreeFlow);
            Assert.All(Descendants<Thumb>(editor), thumb => Assert.Equal(Visibility.Collapsed, thumb.Visibility));
            Assert.Equal(new Thickness(0), Assert.Single(Descendants<TextBox>(editor)).BorderThickness);
            editor.SetTool(InkTool.Select, Colors.Black, 3);
            Assert.Equal(2, Descendants<Thumb>(editor).Count(thumb => thumb.Visibility == Visibility.Visible));
            editor.SetTool(InkTool.Text, Colors.Black, 3);
            Assert.All(Descendants<Thumb>(editor), thumb => Assert.Equal(Visibility.Collapsed, thumb.Visibility));
            var boxed = editor.AddTextAt(new Point(90, 240), "Explicit text box");
            Assert.False(boxed.IsFreeFlow);
            Assert.Equal(2, Descendants<Thumb>(editor).Count(thumb => thumb.Visibility == Visibility.Visible));
        });
    }

    [Fact]
    public void RuledTypingSnapsBaselineAndEveryLineWithLargerFontsUsingMultipleRules()
    {
        Sta(() =>
        {
            var page = new NotePage { Template = PaperTemplate.Ruled };
            var editor = Editor(page);
            var text = editor.AddFreeTextAt(new Point(72, 82), "First line\nSecond line\n第三行");
            Layout(editor);
            Assert.Equal(32, text.LineSpacing);
            Assert.Equal(80, text.Y + FreeTypingLayout.Baseline(text), 5);
            var box = Assert.Single(Descendants<TextBox>(editor));
            Assert.Equal(32, System.Windows.Documents.Block.GetLineHeight(box));
            var first = box.GetRectFromCharacterIndex(0);
            var second = box.GetRectFromCharacterIndex(11);
            Assert.Equal(32, second.Top - first.Top, 2);
            var native = new RenderTargetBitmap((int)Math.Ceiling(editor.Width), (int)Math.Ceiling(editor.Height), 96, 96, PixelFormats.Pbgra32);
            native.Render(editor);
            var baselines = Descendants<Visual>(box)
                .SelectMany(visual => GlyphBaselines(VisualTreeHelper.GetDrawing(visual))
                    .Select(point => visual.TransformToAncestor(editor).Transform(point).Y)).ToArray();
            Assert.NotEmpty(baselines);
            Assert.All(baselines, y => Assert.InRange(RuleDistance(y), 0, .5));
            editor.ApplyTextStyle(fontSize: 36);
            Assert.Equal(64, text.LineSpacing);
            Assert.Equal(64, System.Windows.Documents.Block.GetLineHeight(box));
            Assert.InRange(RuleDistance(text.Y + FreeTypingLayout.Baseline(text)), 0, .001);
            Assert.False(TextFlow.IsOverflowing(page, text));
        });
    }

    [Fact]
    public void ExtendedMarginsRemainFreeWhileOriginalRuledAreaUsesShiftedGuides()
    {
        Sta(() =>
        {
            var page = new NotePage
            {
                Template = PaperTemplate.Ruled, Width = 1000, Height = 1400,
                PaperLayout = new PaperPageLayout { X = 100, Y = 100, Width = 793, Height = 1122 }
            };
            var editor = Editor(page);
            var margin = editor.AddFreeTextAt(new Point(25, 82), "Margin text");
            Assert.Equal(0, margin.LineSpacing);
            Assert.Equal(82, margin.Y);
            var ruled = editor.AddFreeTextAt(new Point(180, 185), "On the original sheet");
            Assert.Equal(32, ruled.LineSpacing);
            Assert.Equal(180, ruled.Y + FreeTypingLayout.Baseline(ruled), 5);
        });
    }

    [Fact]
    public void MeetingPaperHasAllSectionsAndTypingSharesThePageWithEditableInk()
    {
        Sta(() =>
        {
            var page = new NotePage { Template = PaperTemplate.Meeting };
            Assert.Equal(6, (int)PaperTemplate.Meeting);
            var labels = PaperPattern.Labels(page.Template, page.Width, page.Height).Select(label => label.Text).ToArray();
            Assert.Contains("MEETING NOTES", labels);
            Assert.Contains("TITLE", labels);
            Assert.Contains("DATE / TIME", labels);
            Assert.Contains("ATTENDEES", labels);
            Assert.Contains("AGENDA", labels);
            Assert.Contains("DISCUSSION / NOTES", labels);
            Assert.Contains("DECISIONS", labels);
            Assert.Contains("ACTION ITEMS  /  OWNER  /  DUE DATE", labels);
            var editor = Editor(page);
            var text = editor.BeginTyping();
            Assert.Equal(104, text.Y + FreeTypingLayout.Baseline(text), 5);
            Assert.Single(Descendants<TextBox>(editor)).Text = "Weekly planning";
            editor.SetTool(InkTool.Pen, Colors.DarkBlue, 3);
            editor.InkCanvas.Strokes.Add(new Stroke(new StylusPointCollection { new StylusPoint(120, 368), new StylusPoint(240, 370) }));
            editor.CommitPendingEdits();
            Assert.NotEmpty(page.InkData);
            Assert.Equal("Weekly planning", Assert.Single(page.Texts).Text);
            var restored = JsonSerializer.Deserialize<NotePage>(JsonSerializer.Serialize(page, DocumentJson.Options), DocumentJson.Options)!;
            Assert.True(restored.Texts[0].IsFreeFlow);
            Assert.Equal(text.LineSpacing, restored.Texts[0].LineSpacing);
            Assert.Equal(page.InkData, restored.InkData);
            Assert.Equal(PaperTemplate.Meeting, restored.Template);
        });
    }

    [Fact]
    public void FlowTextMeasuresTheSameSpacingWhenCheckingAndContinuingOverflow()
    {
        Sta(() =>
        {
            var page = new NotePage { Width = 420, Height = 360, Template = PaperTemplate.Ruled };
            var editor = Editor(page);
            var original = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"Note {i}"));
            var text = editor.AddFreeTextAt(new Point(72, 80), original);
            Assert.True(TextFlow.IsOverflowing(page, text));
            var continuation = Assert.IsType<TextFlow.Continuation>(TextFlow.CreateContinuation(page, text));
            Assert.Equal(original, continuation.RetainedText + continuation.Text.Text);
            Assert.True(continuation.Text.IsFreeFlow);
            Assert.Equal(32, continuation.Text.LineSpacing);
            Assert.InRange(RuleDistance(continuation.Text.Y + FreeTypingLayout.Baseline(continuation.Text)), 0, .001);
            var restored = page.Snapshot();
            Assert.True(restored.Texts[0].IsFreeFlow);
            Assert.Equal(text.LineSpacing, restored.Texts[0].LineSpacing);
        });
    }

    private static double RuleDistance(double baseline) => Math.Abs(baseline - 80 - Math.Round((baseline - 80) / 32) * 32);

    private static IEnumerable<Point> GlyphBaselines(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun.BaselineOrigin;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var point in GlyphBaselines(child)) yield return group.Transform?.Transform(point) ?? point;
    }

    private static PageEditor Editor(NotePage page) => new(page, _ => throw new InvalidOperationException("No assets"));
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Layout(PageEditor editor)
    {
        editor.Measure(new Size(editor.Width, editor.Height));
        editor.Arrange(new Rect(0, 0, editor.Width, editor.Height));
        editor.UpdateLayout();
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}