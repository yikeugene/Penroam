using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Moye.Controls;
using Moye.Models;

namespace Moye.Tests;

public sealed class TextFlowTests
{
    [Fact]
    public void FullDocumentScanFindsOverflowOnUnrealizedPagesAndDoesNotMutateText()
    {
        Sta(() =>
        {
            var pages = Enumerable.Range(1, 30).Select(_ => new NotePage()).ToList();
            var text = new NoteText { Text = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"{i}. Item")), Height = 60 };
            pages[25].Texts.Add(text);
            var original = text with { };
            var result = Assert.Single(TextFlow.FindOverflow(pages));
            Assert.Equal(pages[25].Id, result.PageId); Assert.Equal(text.Id, result.TextId); Assert.Equal(26, result.PageNumber);
            Assert.Equal(original, text);
        });
    }

    [Fact]
    public void ContinuationPreservesEveryCharacterListMarkerAndStyleWithoutSplittingGraphemes()
    {
        Sta(() =>
        {
            var source = string.Join("\r\n", Enumerable.Range(1, 12).Select(i => $"{i}. 繁體中文 e\u0301 👩‍💻 notes"));
            var text = new NoteText { Text = source, X = 50, Y = 60, Width = 250, Height = 130, FontFamily = "Arial", FontSize = 22, Bold = true, Italic = true, Alignment = NoteTextAlignment.Right, Color = "#FF112233" };
            var page = new NotePage { Width = 400, Height = 240, Texts = [text] };
            var continuation = TextFlow.CreateContinuation(page, text)!;
            Assert.NotEmpty(continuation.RetainedText); Assert.NotEmpty(continuation.Text.Text);
            Assert.Equal(source, continuation.RetainedText + continuation.Text.Text);
            Assert.Contains(continuation.RetainedText.Length, StringInfo.ParseCombiningCharacters(source));
            Assert.EndsWith("\r\n", continuation.RetainedText, StringComparison.Ordinal);
            Assert.Equal(source, text.Text);
            Assert.Equal(text.FontFamily, continuation.Text.FontFamily); Assert.Equal(text.FontSize, continuation.Text.FontSize);
            Assert.Equal(text.Color, continuation.Text.Color); Assert.True(continuation.Text.Bold); Assert.True(continuation.Text.Italic);
            Assert.Equal(text.Alignment, continuation.Text.Alignment); Assert.NotEqual(text.Id, continuation.Text.Id);
            var retained = text with { Text = continuation.RetainedText };
            Assert.False(TextFlow.IsOverflowing(page, retained));
        });
    }

    [Fact]
    public void NonOverflowingTextHasNoContinuationAndTinyFrameMovesAllTextSafely()
    {
        Sta(() =>
        {
            var text = new NoteText { Text = "short", Width = 200, Height = 100 };
            var page = new NotePage { Texts = [text] };
            Assert.Null(TextFlow.CreateContinuation(page, text));
            text.Height = 1;
            var continuation = TextFlow.CreateContinuation(page, text)!;
            Assert.Equal("", continuation.RetainedText); Assert.Equal("short", continuation.Text.Text);
        });
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Text flow test exceeded 30 seconds.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
