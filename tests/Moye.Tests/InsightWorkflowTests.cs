using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Moye.Models;
using Moye.Services;
using Moye.ViewModels;

namespace Moye.Tests;

public sealed class InsightWorkflowTests
{
    [Fact]
    public void RegionRestoreRetainsLaterWorkOutsideAndCrossingObjects()
    {
        Sta(() =>
        {
            var previous = new NotePage { Texts = [new() { X = 20, Y = 20, Width = 60, Height = 50, Text = "earlier" }] };
            var current = previous.Snapshot(); current.Texts[0].Text = "changed";
            current.Texts.Add(new() { X = 200, Y = 200, Width = 80, Height = 60, Text = "later elsewhere" });
            current.Texts.Add(new() { X = 90, Y = 20, Width = 80, Height = 50, Text = "crossing edge" });
            previous.InkData = Ink(Stroke(20, 90, 40, 92, Colors.Blue));
            current.InkData = Ink(Stroke(30, 80, 40, 90, Colors.Red), Stroke(90, 85, 120, 85, Colors.Green), Stroke(300, 300, 310, 310, Colors.Purple));
            var before = JsonSerializer.Serialize(current, DocumentJson.Options);
            var restored = RegionRestoreService.Restore(current, previous, new Rect(0, 0, 100, 100));
            Assert.Contains(restored.Texts, t => t.Text == "earlier");
            Assert.Contains(restored.Texts, t => t.Text == "later elsewhere");
            Assert.Contains(restored.Texts, t => t.Text == "crossing edge");
            var strokes = new StrokeCollection(new MemoryStream(restored.InkData));
            Assert.Equal(3, strokes.Count);
            Assert.Contains(strokes, s => s.DrawingAttributes.Color == Colors.Green);
            Assert.Contains(strokes, s => s.DrawingAttributes.Color == Colors.Blue);
            Assert.DoesNotContain(strokes, s => s.DrawingAttributes.Color == Colors.Red);
            Assert.Equal(before, JsonSerializer.Serialize(current, DocumentJson.Options));
        });
    }

    [Fact]
    public void RestoreDoesNotDuplicateAnObjectMovedOutsideRegion()
    {
        var old = new NotePage { Texts = [new() { X = 10, Y = 10, Width = 40, Height = 40, Text = "move me" }] };
        var current = old.Snapshot(); current.Texts[0].X = 300;
        var restored = RegionRestoreService.Restore(current, old, new Rect(0, 0, 100, 100));
        Assert.Single(restored.Texts); Assert.Equal(300, restored.Texts[0].X);
        old.Width += 1; Assert.Throws<InvalidOperationException>(() => RegionRestoreService.Restore(current, old, new Rect(0, 0, 100, 100)));
    }

    [Fact]
    public void SessionHistoryRestoreIsOneUndoStepAndSnapshotsCannotChangeHistory()
    {
        using var model = new MainViewModel(new WorkflowRepository());
        var page = new NotePage { Texts = [new() { X = 10, Y = 10, Width = 40, Height = 40, Text = "first" }] };
        model.ReplaceDocument(new() { Pages = [page] }, true);
        page.Texts[0].Text = "second"; model.Changed();
        page.Texts.Add(new() { X = 300, Y = 300, Text = "keep" }); model.Changed();
        var versions = model.RegionHistory(page.Id);
        Assert.Equal(2, versions.Count);
        Assert.True(model.RestoreRegion(page.Id, versions[^1], new Rect(0, 0, 100, 100)));
        Assert.Contains(model.Document!.Pages[0].Texts, t => t.Text == "keep");
        Assert.Equal("first", model.Document.Pages[0].Texts[0].Text == "keep" ? model.Document.Pages[0].Texts[1].Text : model.Document.Pages[0].Texts[0].Text);
        model.Undo(); Assert.Equal("second", model.Document!.Pages[0].Texts[0].Text);
        model.Redo(); Assert.Contains(model.Document!.Pages[0].Texts, t => t.Text == "first");
    }

    [Theory]
    [InlineData("#FF0000", NoteColor.Red)]
    [InlineData("#FFFF00", NoteColor.Yellow)]
    [InlineData("#008000", NoteColor.Green)]
    [InlineData("#0000FF", NoteColor.Blue)]
    [InlineData("#800080", NoteColor.Purple)]
    [InlineData("#111111", NoteColor.Black)]
    [InlineData("#AAAAAA", NoteColor.Gray)]
    public void SearchClassifiesRememberedColors(string color, NoteColor expected) =>
        Assert.Equal(expected, VisualNoteSearch.Classify((Color)ColorConverter.ConvertFromString(color)));

    [Fact]
    public void VisualSearchCombinesSectionColorMarkAndPagePosition()
    {
        Sta(() =>
        {
            var section = new NoteSection { Title = "Math" }; var other = new NoteSection { Title = "Other" };
            var highlight = Stroke(600, 920, 650, 930, Colors.Yellow); highlight.DrawingAttributes.IsHighlighter = true;
            var page = new NotePage { SectionId = section.Id, InkData = Ink(highlight, Stroke(20, 20, 40, 40, Colors.Red)),
                Texts = [new() { X = 20, Y = 20, Width = 60, Height = 40, Text = "red note", Color = "#FF0000" }] };
            var book = new NotebookDocument { Sections = [section, other], Pages = [page] };
            var match = Assert.Single(VisualNoteSearch.Find(book, NoteColor.Yellow, NotePosition.BottomRight, NoteMark.Highlighter, section.Id));
            Assert.Equal(page.Id, match.PageId); Assert.Equal(1, match.Count);
            Assert.Empty(VisualNoteSearch.Find(book, NoteColor.Yellow, NotePosition.TopLeft, NoteMark.Any));
            Assert.Empty(VisualNoteSearch.Find(book, NoteColor.Any, NotePosition.Anywhere, NoteMark.Any, other.Id));
            Assert.Equal(1, Assert.Single(VisualNoteSearch.Find(book, NoteColor.Red, NotePosition.TopLeft, NoteMark.TypedText)).Count);
            var cancelled = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => VisualNoteSearch.Find(book, NoteColor.Any, NotePosition.Anywhere, NoteMark.Any, token: cancelled));
        });
    }

    [Fact]
    public void PracticeCreatesIndependentWorkWithSharedQuestionAndUndo()
    {
        using var model = new MainViewModel(new WorkflowRepository());
        var page = new NotePage { Texts = [new() { Text = "Original solution" }] };
        model.ReplaceDocument(new() { Pages = [page] }, true);
        Assert.True(model.AddPracticeAttempt(page.Id, new string('a', 64), 500, 260, "Substitution"));
        var first = model.SelectedPage!.Page;
        Assert.True(Moye.Controls.FreeTypingLayout.StartPoint(first).Y > first.Images[0].Y + first.Images[0].Height);
        Assert.Equal(page.Id, first.Practice!.SourcePageId); Assert.Empty(first.Texts); Assert.Empty(first.InkData);
        Assert.True(model.SetPracticeReflection(first.Id, "Forgot to substitute back", true));
        Assert.True(model.AddPracticeAttempt(first.Id, first.Practice.QuestionAssetId, first.Practice.QuestionWidth, first.Practice.QuestionHeight, first.Practice.Question, first.Practice.GroupId));
        var second = model.SelectedPage!.Page;
        Assert.Equal(2, second.Practice!.Number); Assert.Equal(first.Images[0].AssetId, second.Images[0].AssetId);
        Assert.Equal(first.Images[0].Width, second.Images[0].Width);
        Assert.Empty(second.Practice.Reflection); Assert.Equal("Forgot to substitute back", first.Practice.Reflection);
        var snapshot = first.Snapshot(); snapshot.Practice!.Reflection = "isolated";
        Assert.NotEqual(snapshot.Practice.Reflection, first.Practice.Reflection);
        model.Undo(); Assert.Equal(2, model.Document!.Pages.Count);
        model.Redo(); Assert.Equal(3, model.Document!.Pages.Count);
    }

    [Fact]
    public async Task BackupRetainsQuestionAssetEvenIfImageDeletedAndRemapsOriginalPage()
    {
        using var directory = new StorageTestDirectory(); using var targetDirectory = new StorageTestDirectory();
        using var source = new SqliteNotebookRepository(directory.DatabasePath); using var target = new SqliteNotebookRepository(targetDirectory.DatabasePath);
        var asset = await source.PutAssetAsync("question.png", "image/png", [1, 2, 3, 4]);
        var origin = new NotePage();
        var attempt = new NotePage { Practice = new() { SourcePageId = origin.Id, QuestionAssetId = asset.Id, Question = "Question", QuestionWidth = 500, QuestionHeight = 300, Reflection = "Reflection" },
            Texts = [new() { Text = "Free text", IsFreeFlow = true, LineSpacing = 32 }] };
        var book = new NotebookDocument { Pages = [origin, attempt] };
        await source.SaveAsync(book);
        var saved = (await source.LoadAsync(book.Id))!; Assert.Equal(attempt.Practice, saved.Pages[1].Practice);
        var path = Path.Combine(directory.Root, "practice.moye");
        await new BackupService(source).ExportAsync(path, [book]);
        var restored = Assert.Single(await new BackupService(target).ImportAsync(path));
        Assert.Equal(restored.Pages[0].Id, restored.Pages[1].Practice!.SourcePageId);
        Assert.NotEqual(origin.Id, restored.Pages[0].Id);
        Assert.Equal(asset.Bytes, (await target.GetAssetAsync(asset.Id)).Bytes);
        Assert.True(restored.Pages[1].Texts[0].IsFreeFlow); Assert.Equal(32, restored.Pages[1].Texts[0].LineSpacing);
        Assert.Equal("Reflection", restored.Pages[1].Practice!.Reflection);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(2000)]
    public async Task InvalidFreeTypingLineHeightCannotReplaceSavedDocument(double spacing)
    {
        using var directory = new StorageTestDirectory(); using var source = new SqliteNotebookRepository(directory.DatabasePath);
        var book = new NotebookDocument { Pages = [new() { Texts = [new() { Text = "saved" }] }] }; await source.SaveAsync(book);
        book.Pages[0].Texts[0].LineSpacing = spacing;
        await Assert.ThrowsAsync<InvalidDataException>(() => source.SaveAsync(book));
        Assert.Equal(0, (await source.LoadAsync(book.Id))!.Pages[0].Texts[0].LineSpacing);
    }

    private static Stroke Stroke(double x1, double y1, double x2, double y2, Color color) => new(new StylusPointCollection([new StylusPoint(x1, y1), new StylusPoint(x2, y2)]))
    { DrawingAttributes = new DrawingAttributes { Color = color, Width = 2, Height = 2 } };
    private static byte[] Ink(params Stroke[] strokes) { using var stream = new MemoryStream(); new StrokeCollection(strokes).Save(stream); return stream.ToArray(); }
    private static void Sta(Action action)
    {
        Exception? error = null; var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
