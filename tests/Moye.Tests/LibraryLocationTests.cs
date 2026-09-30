using System.IO;
using Moye.Models;
using Moye.Services;

namespace Moye.Tests;

public sealed class LibraryLocationTests
{
    [Fact]
    public async Task PenroamReopensLegacyMoyeNotebooksAndPreferencesInPlace()
    {
        using var directory = new StorageTestDirectory();
        var legacyDirectory = Path.Combine(directory.Root, "Moye");
        var legacyDatabase = Path.Combine(legacyDirectory, "moye.db");
        var notebook = new NotebookDocument
        {
            Title = "Existing notes 舊筆記",
            Pages = [new NotePage { Texts = [new NoteText { Text = "Keep this content unchanged." }] }]
        };
        using (var oldRepository = new SqliteNotebookRepository(legacyDatabase))
        {
            await oldRepository.InitializeAsync();
            await oldRepository.SaveAsync(notebook);
        }
        var preferences = WritingPreferences.CreateDefault();
        preferences.Presets[0].Name = "My existing pen";
        var legacyPreferences = Path.Combine(legacyDirectory, "writing-preferences.json");
        await new WritingPreferencesStore(legacyPreferences).SaveAsync(preferences);
        var originalPreferences = await File.ReadAllBytesAsync(legacyPreferences);

        var location = LibraryLocation.Resolve(localApplicationData: directory.Root);
        using var reopened = new SqliteNotebookRepository(location.DatabasePath);
        await reopened.InitializeAsync();
        var loaded = await reopened.LoadAsync(notebook.Id);
        Assert.NotNull(loaded);
        Assert.Equal(notebook.Title, loaded.Title);
        Assert.Equal("Keep this content unchanged.", Assert.Single(Assert.Single(loaded.Pages).Texts).Text);
        var loadedPreferences = await new WritingPreferencesStore(location.PreferencesPath).LoadAsync();
        Assert.Null(loadedPreferences.Warning);
        Assert.Equal("My existing pen", loadedPreferences.Preferences.Presets[0].Name);
        Assert.Equal(originalPreferences, await File.ReadAllBytesAsync(legacyPreferences));
        Assert.False(Directory.Exists(Path.Combine(directory.Root, "Penroam")));
    }

    [Fact]
    public void DefaultDatabasePathRemainsCompatibleWithExistingRepository()
    {
        using var repository = new SqliteNotebookRepository();
        var location = LibraryLocation.Resolve();
        Assert.Equal(repository.DatabasePath, location.DatabasePath);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(repository.DatabasePath)!, "writing-preferences.json"), location.PreferencesPath);
    }

    [Fact]
    public void DefaultAndExplicitDefaultDirectoryShareTheSameLiveMutex()
    {
        var localAppData = Path.Combine(Path.GetTempPath(), "Moye-library-identity", Guid.NewGuid().ToString("N"));
        var defaultLocation = LibraryLocation.Resolve(localApplicationData: localAppData);
        var explicitLocation = LibraryLocation.Resolve(Path.Combine(localAppData, "Moye") + Path.DirectorySeparatorChar);
        Assert.Equal(defaultLocation.DatabasePath, explicitLocation.DatabasePath);
        Assert.Equal(defaultLocation.PreferencesPath, explicitLocation.PreferencesPath);
        Assert.Equal(defaultLocation.MutexName, explicitLocation.MutexName);

        using var first = new Mutex(true, defaultLocation.MutexName, out var firstCreated);
        try
        {
            Assert.True(firstCreated);
            using var second = new Mutex(false, explicitLocation.MutexName, out var secondCreated);
            Assert.False(secondCreated);
        }
        finally { first.ReleaseMutex(); }
    }

    [Fact]
    public void CaseSlashesAndDotSegmentsDoNotCreateAnotherWriterIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Moye-library-identity", Guid.NewGuid().ToString("N"), "MixedCase");
        var baseline = LibraryLocation.Resolve(directory);
        var upperCase = LibraryLocation.Resolve(directory.ToUpperInvariant());
        var slashVariant = LibraryLocation.Resolve(directory.Replace('\\', '/') + "/");
        var dotVariant = LibraryLocation.Resolve(Path.Combine(directory, "temporary", "..", "."));
        Assert.Equal(baseline.MutexName, upperCase.MutexName);
        Assert.Equal(baseline.MutexName, slashVariant.MutexName);
        Assert.Equal(baseline.MutexName, dotVariant.MutexName);
        Assert.Equal(baseline.DatabasePath, dotVariant.DatabasePath);
    }

    [Fact]
    public void ExplicitLibrariesIsolateTheirDatabasePreferencesAndMutex()
    {
        var root = Path.Combine(Path.GetTempPath(), "Moye-library-identity", Guid.NewGuid().ToString("N"));
        var first = LibraryLocation.Resolve(Path.Combine(root, "first"));
        var second = LibraryLocation.Resolve(Path.Combine(root, "second"));
        Assert.NotEqual(first.DatabasePath, second.DatabasePath);
        Assert.NotEqual(first.PreferencesPath, second.PreferencesPath);
        Assert.NotEqual(first.MutexName, second.MutexName);
        Assert.Equal(Path.GetDirectoryName(first.DatabasePath), Path.GetDirectoryName(first.PreferencesPath));
        Assert.Equal(Path.GetDirectoryName(second.DatabasePath), Path.GetDirectoryName(second.PreferencesPath));
    }
}
