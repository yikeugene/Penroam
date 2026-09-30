using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moye.Models;

namespace Moye.Services;

public sealed record RecoveryDraft(int Version, long Revision, DateTimeOffset CapturedUtc, NotebookDocument Document);
public sealed record RecoveryDraftFile(string Path, RecoveryDraft Draft);

/// <summary>Atomic, local pending snapshots. A committed revision can never remove a newer draft.</summary>
public sealed class DraftRecoveryStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, long> _committed = [];
    private readonly Dictionary<string, Task> _writes = [];
    private readonly Dictionary<string, (NotebookDocument Document, long Revision)> _latest = [];
    private readonly object _sync = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? LastError { get; private set; }
    public DateTimeOffset? LastCapturedUtc { get; private set; }

    private string DraftPath(string id) => Path.Combine(DirectoryPath,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + "." + _session + ".draft.json");

    public void Queue(NotebookDocument snapshot, long revision)
    {
        // Snapshot is already isolated by AutosaveCoordinator, and never mutated by this service.
        lock (_sync)
        {
            _latest[snapshot.Id] = (snapshot, revision);
            if (_writes.ContainsKey(snapshot.Id)) return;
            _writes[snapshot.Id] = Task.Run(async () =>
            {
                while (true)
                {
                    (NotebookDocument Document, long Revision) pending;
                    lock (_sync)
                    {
                        if (!_latest.Remove(snapshot.Id, out pending)) { _writes.Remove(snapshot.Id); return; }
                    }
                    try { await WriteAsync(pending.Document, pending.Revision); }
                    catch (Exception ex) { LastError = ex.Message; }
                }
            });
        }
    }

    public async Task WriteAsync(NotebookDocument document, long revision)
    {
        await _gate.WaitAsync();
        try
        {
            if (_committed.GetValueOrDefault(document.Id) >= revision) return;
            var path = DraftPath(document.Id);
            if (File.Exists(path))
            {
                var previous = JsonSerializer.Deserialize<RecoveryDraft>(await File.ReadAllTextAsync(path), DocumentJson.Options);
                if (previous is { Version: not 1 }) throw new IOException("A newer recovery format was preserved.");
                if (previous is not null && previous.Revision > revision) return;
            }
            var captured = DateTimeOffset.UtcNow;
            await AtomicLocalFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(new RecoveryDraft(1, revision, captured, document), DocumentJson.Options));
            LastCapturedUtc = captured; LastError = null;
        }
        finally { _gate.Release(); }
    }

    public async Task MarkCommittedAsync(string id, long revision)
    {
        await _gate.WaitAsync();
        try
        {
            _committed[id] = Math.Max(_committed.GetValueOrDefault(id), revision);
            var path = DraftPath(id);
            if (File.Exists(path))
            {
                var draft = JsonSerializer.Deserialize<RecoveryDraft>(await File.ReadAllTextAsync(path), DocumentJson.Options);
                if (draft is { Version: 1 } && draft.Revision <= revision) File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { LastError = ex.Message; }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<RecoveryDraftFile>> ReadAsync()
    {
        var results = new List<RecoveryDraftFile>();
        if (!Directory.Exists(DirectoryPath)) return results;
        string[] paths;
        try { paths = Directory.GetFiles(DirectoryPath, "*.draft.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastError = "Recovery files could not be listed. " + ex.Message; return results; }
        foreach (var path in paths)
        {
            try
            {
                var draft = JsonSerializer.Deserialize<RecoveryDraft>(await File.ReadAllTextAsync(path), DocumentJson.Options);
                if (draft is { Version: 1, Document: not null }) results.Add(new(path, draft));
                else LastError = "An unsupported recovery file was kept: " + Path.GetFileName(path);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { LastError = "A recovery file could not be read and was kept. " + ex.Message; }
        }
        return results.OrderByDescending(item => item.Draft.CapturedUtc).ToArray();
    }

    public async Task<bool> ForgetAsync(RecoveryDraftFile file)
    {
        // Only files returned from this recovery directory can be removed.
        var expectedPrefix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file.Draft.Document.Id))) + ".";
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file.Path)), DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(file.Path).StartsWith(expectedPrefix, StringComparison.Ordinal) || !file.Path.EndsWith(".draft.json", StringComparison.Ordinal))
            throw new IOException("The draft is outside this library's recovery directory.");
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(file.Path)) return true;
            var current = JsonSerializer.Deserialize<RecoveryDraft>(await File.ReadAllTextAsync(file.Path), DocumentJson.Options);
            if (current is null || current.Version != 1 || current.Revision != file.Draft.Revision || current.CapturedUtc != file.Draft.CapturedUtc)
                return false;
            File.Delete(file.Path); return true;
        }
        finally { _gate.Release(); }
    }

    public async Task DrainAsync()
    {
        Task[] writes; lock (_sync) writes = _writes.Values.ToArray();
        await Task.WhenAll(writes);
    }
}
