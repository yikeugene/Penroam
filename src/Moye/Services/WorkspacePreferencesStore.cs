using System.IO;
using System.Text.Json;
using Moye.Models;
using Moye.Localization;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

public sealed class WorkspacePreferencesStore(string path)
{
    private readonly SemaphoreSlim _gate = new(1);
    private bool _read, _blocked;
    public string FilePath { get; } = Path.GetFullPath(path);
    private LocalizedMessage? _warning;
    public string? Warning => _warning?.ToString();
    public LocalizedMessage? WarningMessage => _warning;

    public async Task<WorkspacePreferences> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _read = true;
            if (!File.Exists(FilePath)) return new();
            var state = JsonSerializer.Deserialize<WorkspacePreferences>(await File.ReadAllTextAsync(FilePath), DocumentJson.Options)
                ?? throw new JsonException(L.Text("Core.WorkspaceMissing"));
            if (state.Version != 1) throw new NotSupportedException(L.Text("Core.WorkspaceVersionUnsupported", state.Version));
            state.UiLanguage = L.NormalizeLanguage(state.UiLanguage);
            state.ReadingPositions ??= [];
            state.ReadingPositions = state.ReadingPositions.Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is not null).ToDictionary(item => item.Key, item => item.Value);
            state.LastNotebookId ??= ""; state.BackupDirectory ??= ""; state.LastBackupError ??= "";
            state.BackupIntervalHours = Math.Clamp(state.BackupIntervalHours, 1, 168);
            state.BackupRetention = Math.Clamp(state.BackupRetention, 1, 100);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _blocked = true;
            _warning = L.M("Core.WorkspaceLoadFailed", ex.Message);
            return new();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WorkspacePreferences state)
    {
        // Serialize on the caller's thread before any asynchronous work touches mutable preferences.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, DocumentJson.Options);
        await _gate.WaitAsync();
        try
        {
            if (!_read || _blocked) throw new IOException(Warning ?? L.Text("Core.WorkspaceReadFirst"));
            await AtomicLocalFile.WriteAsync(FilePath, bytes);
        }
        finally { _gate.Release(); }
    }
}

internal static class AtomicLocalFile
{
    internal static async Task WriteAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync(); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
