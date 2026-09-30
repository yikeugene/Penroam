using System.IO;
using System.Text.Json;
using System.Windows;
using Moye.Controls;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class WorkspaceSafetyTests
{
    [Fact]
    public async Task ReadingPreferencesRoundTripWithoutChangingNotebook()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "workspace.json");
        var store = new WorkspacePreferencesStore(path);
        var preferences = await store.LoadAsync();
        preferences.ReadingPositions["note"] = new("section", "page", .2, .6, .5, .1, 1.3, true, DateTimeOffset.UtcNow);
        preferences.LastNotebookId = "note"; preferences.TwoFingerNavigationOnly = true;
        preferences.BackupRetention = 7; preferences.FocusToolsOnRight = true;
        await store.SaveAsync(preferences);
        var reopened = await new WorkspacePreferencesStore(path).LoadAsync();
        Assert.Equal(preferences.ReadingPositions["note"], reopened.ReadingPositions["note"]);
        Assert.True(reopened.TwoFingerNavigationOnly); Assert.True(reopened.FocusToolsOnRight);
        Assert.Equal(7, reopened.BackupRetention);
        Assert.False(File.Exists(directory.DatabasePath));
    }

    [Fact]
    public async Task NullReadingPositionAndOptionalStringsDoNotPreventLoadingValidPreferences()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "workspace.json");
        const string content = """
            {
              "version": 1,
              "readingPositions": {
                "missing": null,
                "valid": {
                  "sectionId": "section", "pageId": "page", "x": 0.2, "y": 0.6,
                  "viewX": 0.5, "viewY": 0.1, "zoom": 1.3, "fitWidth": true,
                  "viewedUtc": "2026-09-30T08:00:00+00:00"
                }
              },
              "lastNotebookId": null,
              "backupDirectory": null,
              "lastBackupError": null,
              "twoFingerNavigationOnly": true
            }
            """;
        await File.WriteAllTextAsync(path, content);
        var store = new WorkspacePreferencesStore(path);

        var preferences = await store.LoadAsync();

        Assert.Null(store.Warning);
        var position = Assert.Single(preferences.ReadingPositions);
        Assert.Equal("valid", position.Key); Assert.Equal("page", position.Value.PageId);
        Assert.Equal(1.3, position.Value.Zoom); Assert.True(position.Value.FitWidth);
        Assert.True(preferences.TwoFingerNavigationOnly);
        Assert.Equal("", preferences.LastNotebookId); Assert.Equal("", preferences.BackupDirectory);
        Assert.Equal("", preferences.LastBackupError);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
        await store.SaveAsync(preferences);
        var reopened = await new WorkspacePreferencesStore(path).LoadAsync();
        Assert.Equal(position.Value, Assert.Single(reopened.ReadingPositions).Value);
        Assert.True(reopened.TwoFingerNavigationOnly);
    }

    [Theory]
    [InlineData("{\"version\":42,\"keep\":\"future settings\"}")]
    [InlineData("not json")]
    public async Task UnreadableOrFutureWorkspaceFileIsNeverOverwritten(string content)
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "workspace.json"); await File.WriteAllTextAsync(path, content);
        var store = new WorkspacePreferencesStore(path); var preferences = await store.LoadAsync();
        Assert.NotNull(store.Warning);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(preferences));
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CommittingOlderRevisionKeepsNewerDurableDraft()
    {
        using var directory = new StorageTestDirectory();
        var store = new DraftRecoveryStore(Path.Combine(directory.Root, "drafts"));
        var notebook = new NotebookDocument { Title = "Old", Pages = [new()] };
        await store.WriteAsync(notebook, 1);
        notebook.Title = "New"; await store.WriteAsync(notebook, 2);
        await store.MarkCommittedAsync(notebook.Id, 1);
        Assert.Equal("New", Assert.Single(await store.ReadAsync()).Draft.Document.Title);
        await store.MarkCommittedAsync(notebook.Id, 2);
        await store.WriteAsync(notebook, 1); // A late asynchronous write must not resurrect saved data.
        Assert.Empty(await store.ReadAsync());
    }

    [Fact]
    public async Task NewSessionCommitDoesNotDiscardPreviousCrashDraft()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "drafts");
        var notebook = new NotebookDocument { Title = "Recovered", Pages = [new() { Texts = [new() { Text = "香港\nLecture notes" }] }] };
        await new DraftRecoveryStore(path).WriteAsync(notebook, 78);
        var reopened = new DraftRecoveryStore(path);
        await reopened.WriteAsync(notebook, 1); await reopened.MarkCommittedAsync(notebook.Id, 1);
        var remaining = Assert.Single(await reopened.ReadAsync());
        Assert.Equal(78, remaining.Draft.Revision);
        Assert.Equal("香港\nLecture notes", remaining.Draft.Document.Pages[0].Texts[0].Text);
        await reopened.ForgetAsync(remaining); Assert.Empty(await reopened.ReadAsync());
    }

    [Fact]
    public async Task ForgettingAStaleRecoveryListEntryKeepsTheNewerDraftByteForByte()
    {
        using var directory = new StorageTestDirectory();
        var store = new DraftRecoveryStore(Path.Combine(directory.Root, "drafts"));
        var notebook = new NotebookDocument { Title = "Initial", Pages = [new() { Texts = [new() { Text = "Original" }] }] };
        await store.WriteAsync(notebook, 1);
        var displayedEntry = Assert.Single(await store.ReadAsync());
        notebook.Title = "Latest"; notebook.Pages[0].Texts[0].Text = "New unsaved content";
        await store.WriteAsync(notebook, 2);
        var latestBytes = await File.ReadAllBytesAsync(displayedEntry.Path);

        Assert.False(await store.ForgetAsync(displayedEntry));

        Assert.Equal(latestBytes, await File.ReadAllBytesAsync(displayedEntry.Path));
        var retained = Assert.Single(await store.ReadAsync());
        Assert.Equal(2, retained.Draft.Revision);
        Assert.Equal("New unsaved content", retained.Draft.Document.Pages[0].Texts[0].Text);
        Assert.True(await store.ForgetAsync(retained));
        Assert.Empty(await store.ReadAsync());
    }

    [Fact]
    public async Task QueuedDraftsRemainInOrderAndReopenAfterInterruptedSession()
    {
        using var directory = new StorageTestDirectory();
        var path = Path.Combine(directory.Root, "drafts"); var store = new DraftRecoveryStore(path);
        var document = new NotebookDocument { Title = "First" };
        store.Queue(document.Snapshot(), 1); document.Title = "Second"; store.Queue(document.Snapshot(), 2);
        await store.DrainAsync();
        Assert.Equal("Second", Assert.Single(await new DraftRecoveryStore(path).ReadAsync()).Draft.Document.Title);
    }

    [Fact]
    public async Task RecentlyDeletedSnapshotRestoresEditableContentAsCopy()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var image = await repository.PutAssetAsync("test.png", "image/png", [1, 5, 8]);
        var notebook = new NotebookDocument { Title = "Before deletion", Pages = [new() { Title = "Exam", IsBookmarked = true, Texts = [new() { Text = "Keep me" }], Images = [new() { AssetId = image.Id }] }] };
        await repository.SaveAsync(notebook);
        var safety = new LocalSafetyService(Path.Combine(directory.Root, "deleted"), new BackupService(repository));
        var entry = await safety.PreserveAsync(notebook, "Page deleted");
        await repository.DeleteAsync(notebook.Id);
        var restored = Assert.Single(await safety.RestoreAsync(entry));
        Assert.NotEqual(notebook.Id, restored.Id);
        Assert.Equal("Exam", restored.Pages[0].Title); Assert.True(restored.Pages[0].IsBookmarked);
        Assert.Equal("Keep me", restored.Pages[0].Texts[0].Text);
        Assert.Equal(image.Bytes, (await repository.GetAssetAsync(restored.Pages[0].Images[0].AssetId)).Bytes);
        Assert.Single(await safety.ListAsync());
    }

    [Fact]
    public async Task AutomaticRetentionOnlyRemovesThisLibrarysCompletedBackups()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var destination = Path.Combine(directory.Root, "backups"); Directory.CreateDirectory(destination);
        var manual = Path.Combine(destination, "my-manual-backup.moye"); await File.WriteAllTextAsync(manual, "keep");
        var backup = new BackupService(repository);
        var notes = new[] { new NotebookDocument { Pages = [new()] } };
        var other = await new ScheduledBackupService(Path.Combine(directory.Root, "other"), backup).CreateAsync(destination, notes, 1);
        var schedule = new ScheduledBackupService(directory.Root, backup);
        var first = await schedule.CreateAsync(destination, notes, 1);
        var second = await schedule.CreateAsync(destination, notes, 1);
        Assert.False(File.Exists(first)); Assert.True(File.Exists(second)); Assert.True(File.Exists(other)); Assert.True(File.Exists(manual));
    }

    [Fact]
    public async Task AutomaticRetentionKeepsTheNewBackupWhenOlderNamesHaveFutureTimestamps()
    {
        using var directory = new StorageTestDirectory();
        using var repository = new SqliteNotebookRepository(directory.DatabasePath);
        var destination = Path.Combine(directory.Root, "backups");
        var backup = new BackupService(repository);
        var schedule = new ScheduledBackupService(directory.Root, backup);
        var notebook = new NotebookDocument { Pages = [new() { Texts = [new() { Text = "Before clock correction" }] }] };
        var previous = await schedule.CreateAsync(destination, [notebook], 1);
        // Simulate a completed backup whose name was created before correcting
        // a clock set far into the future, without changing the system clock.
        var futureName = System.Text.RegularExpressions.Regex.Replace(Path.GetFileName(previous),
            @"-\d{8}-\d{9}-", "-99991231-235959999-");
        Assert.NotEqual(Path.GetFileName(previous), futureName);
        var future = Path.Combine(destination, futureName);
        File.Move(previous, future);
        notebook.Pages[0].Texts[0].Text = "Latest notes after clock correction";

        var created = await schedule.CreateAsync(destination, [notebook], 1);

        Assert.True(File.Exists(created)); Assert.False(File.Exists(future));
        Assert.Equal(created, Assert.Single(Directory.GetFiles(destination, "*.moye")));
        var restored = Assert.Single(await backup.ImportAsync(created));
        Assert.Equal("Latest notes after clock correction", restored.Pages[0].Texts[0].Text);
    }

    [Fact]
    public void ScheduledBackupRespectsIntervalAndOptIn()
    {
        var now = DateTimeOffset.UtcNow;
        var preferences = new WorkspacePreferences { BackupDirectory = "C:\\backups", BackupIntervalHours = 6, LastBackupUtc = now.AddHours(-7) };
        Assert.False(ScheduledBackupService.IsDue(preferences, now));
        preferences.BackupEnabled = true; Assert.True(ScheduledBackupService.IsDue(preferences, now));
        preferences.LastBackupUtc = now; Assert.False(ScheduledBackupService.IsDue(preferences, now));
        preferences.LastBackupUtc = now.AddYears(1); Assert.True(ScheduledBackupService.IsDue(preferences, now));
    }

    [Fact]
    public void TwoFingerGuardDiscardsSingleContactMovementBeforeAndAfterPinch()
    {
        var touch = new TouchNavigationSession { RequireTwoFingers = true };
        touch.BeginContact(1, new(100, 100), 0); touch.MoveContact(1, new(100, 300), 20);
        Assert.False(touch.TryTakeFrame(20, out _));
        touch.BeginContact(2, new(200, 300), 30);
        touch.MoveContact(1, new(100, 310), 40); touch.MoveContact(2, new(200, 310), 40);
        Assert.True(touch.TryTakeFrame(40, out var frame)); Assert.Equal(-10, frame.ScrollDelta.Y);
        touch.EndContact(2, 45); touch.MoveContact(1, new(100, 500), 60);
        Assert.False(touch.TryTakeFrame(60, out _)); touch.EndContact(1, 61); Assert.False(touch.IsInertiaActive);
    }

    [Fact]
    public void ZoomLockAllowsTwoFingerPanWithoutScaleChange()
    {
        var touch = new TouchNavigationSession { ZoomLocked = true };
        touch.BeginContact(1, new(100, 100), 0); touch.BeginContact(2, new(200, 100), 0);
        touch.MoveContact(1, new(50, 120), 20); touch.MoveContact(2, new(250, 120), 20);
        Assert.True(touch.TryTakeFrame(20, out var frame)); Assert.Equal(1, frame.Scale); Assert.Equal(-20, frame.ScrollDelta.Y);
    }
}
