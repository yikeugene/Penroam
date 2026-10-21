using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moye.Localization;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

public sealed record WritingPreferencesLoadResult(
    WritingPreferences Preferences, string? Warning = null, string? PreservedFilePath = null, bool CanSave = true)
{
    private readonly string? _warning = Warning;
    public LocalizedMessage? LocalizedWarning { get; init; }
    public string? Warning
    {
        get => LocalizedWarning?.ToString() ?? _warning;
        init { _warning = value; LocalizedWarning = null; }
    }
}

/// <summary>Serializes local writing preferences using an atomic file replacement.</summary>
public sealed class WritingPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<InkTool>() }
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loadAttempted;
    private LocalizedMessage? _saveBlockedReason;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Moye", "writing-preferences.json");
    public string FilePath { get; }

    public WritingPreferencesStore(string? filePath = null) => FilePath = Path.GetFullPath(filePath ?? DefaultPath);

    public Task<WritingPreferencesLoadResult> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }, cancellationToken);

    public Task SaveAsync(WritingPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Version != WritingPreferences.CurrentVersion)
            throw new InvalidDataException(L.Text("Messages.Writing.UnsupportedVersion"));
        // Capture before queuing: later UI edits must not change this save.
        var snapshot = preferences.Normalize();
        return Task.Run(async () =>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string? temporaryPath = null;
            try
            {
                if (!_loadAttempted)
                {
                    var loaded = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
                    if (loaded.Warning is not null) throw new IOException(loaded.Warning);
                }
                if (_saveBlockedReason is not null) throw new IOException(_saveBlockedReason.ToString());
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, null);
                else File.Move(temporaryPath, FilePath);
                temporaryPath = null;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                _gate.Release();
            }
        }, cancellationToken);
    }

    private async Task<WritingPreferencesLoadResult> LoadCoreAsync(CancellationToken cancellationToken)
    {
        _loadAttempted = true;
        _saveBlockedReason = null;
        string json;
        try { json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { _loadAttempted = false; throw; }
        catch (FileNotFoundException) { return new(WritingPreferences.CreateDefault()); }
        catch (DirectoryNotFoundException) { return new(WritingPreferences.CreateDefault()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _saveBlockedReason = L.M("Messages.Writing.Unreadable", exception.Message);
            return new(WritingPreferences.CreateDefault(), PreservedFilePath: FilePath, CanSave: false) { LocalizedWarning = _saveBlockedReason };
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<WritingPreferences>(json, JsonOptions)
                ?? throw new JsonException(L.Text("Messages.Writing.EmptySettings"));
            if (loaded.Version != WritingPreferences.CurrentVersion)
            {
                _saveBlockedReason = L.M("Messages.Writing.NewerVersion", loaded.Version);
                return new(WritingPreferences.CreateDefault(), PreservedFilePath: FilePath, CanSave: false) { LocalizedWarning = _saveBlockedReason };
            }
            var normalized = loaded.Normalize();
            var changed = JsonSerializer.Serialize(loaded, JsonOptions) != JsonSerializer.Serialize(normalized, JsonOptions);
            return new(normalized) { LocalizedWarning = changed ? L.M("Messages.Writing.ValuesReset") : null };
        }
        catch (JsonException)
        {
            var preservedPath = FilePath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            try { File.Move(FilePath, preservedPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _saveBlockedReason = L.M("Messages.Writing.DamagedNotPreserved", exception.Message);
                return new(WritingPreferences.CreateDefault(), PreservedFilePath: FilePath, CanSave: false) { LocalizedWarning = _saveBlockedReason };
            }
            return new(WritingPreferences.CreateDefault(), PreservedFilePath: preservedPath)
            { LocalizedWarning = L.M("Messages.Writing.DamagedPreserved", preservedPath) };
        }
    }
}
