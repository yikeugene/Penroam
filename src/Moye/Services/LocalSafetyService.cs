using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moye.Models;

namespace Moye.Services;

public sealed record DeletedSnapshot(string Id, string Title, string Reason, DateTimeOffset DeletedUtc, int PageCount)
{
    public DateTimeOffset ExpiresUtc => DeletedUtc.AddDays(30);
    public string Label => $"{Title} · {Reason} · {PageCount} pages · {DeletedUtc.ToLocalTime():g}";
}

public sealed class LocalSafetyService(string directory, IBackupService backup)
{
    private readonly SemaphoreSlim _gate = new(1);
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string? Warning { get; private set; }

    public async Task<DeletedSnapshot> PreserveAsync(NotebookDocument document, string reason)
    {
        var snapshot = document.Snapshot();
        var entry = new DeletedSnapshot(Guid.NewGuid().ToString("N"), snapshot.Title, reason, DateTimeOffset.UtcNow, snapshot.Pages.Count);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await backup.ExportAsync(ArchivePath(entry), [snapshot]);
            await AtomicLocalFile.WriteAsync(MetadataPath(entry), JsonSerializer.SerializeToUtf8Bytes(entry, DocumentJson.Options));
            return entry;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DeletedSnapshot>> ListAsync()
    {
        var list = new List<DeletedSnapshot>();
        if (!Directory.Exists(DirectoryPath)) return list;
        string[] paths;
        try { paths = Directory.GetFiles(DirectoryPath, "*.deleted.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warning = "Recovery files could not be listed. " + ex.Message; return list; }
        foreach (var path in paths)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<DeletedSnapshot>(await File.ReadAllTextAsync(path), DocumentJson.Options);
                if (entry is null || !IsId(entry.Id) || !File.Exists(ArchivePath(entry))) continue;
                if (entry.ExpiresUtc <= DateTimeOffset.UtcNow) { Delete(entry); continue; }
                list.Add(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { Warning = "Some recovery entries could not be read and were kept. " + ex.Message; }
        }
        return list.OrderByDescending(entry => entry.DeletedUtc).ToArray();
    }

    public Task<IReadOnlyList<NotebookDocument>> RestoreAsync(DeletedSnapshot entry, CancellationToken cancellationToken = default)
        => backup.ImportAsync(ArchivePath(entry), cancellationToken);

    public void Delete(DeletedSnapshot entry)
    {
        File.Delete(ArchivePath(entry)); File.Delete(MetadataPath(entry));
    }

    private static bool IsId(string id) => Guid.TryParseExact(id, "N", out _);
    private string ArchivePath(DeletedSnapshot entry) => IsId(entry.Id) ? Path.Combine(DirectoryPath, entry.Id + ".moye") : throw new IOException("Invalid recovery entry.");
    private string MetadataPath(DeletedSnapshot entry) => Path.ChangeExtension(ArchivePath(entry), ".deleted.json");
}

public sealed class ScheduledBackupService(string libraryIdentity, IBackupService backup)
{
    // Keep the established prefix so retention still includes pre-rebrand backups.
    private readonly string _prefix = "Moye-auto-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(libraryIdentity).ToUpperInvariant())))[..12] + "-";

    public static bool IsDue(WorkspacePreferences preferences, DateTimeOffset now) => preferences.BackupEnabled &&
        !string.IsNullOrWhiteSpace(preferences.BackupDirectory) &&
        (preferences.LastBackupUtc is null || preferences.LastBackupUtc > now || now - preferences.LastBackupUtc >= TimeSpan.FromHours(Math.Clamp(preferences.BackupIntervalHours, 1, 168)));

    public async Task<string> CreateAsync(string destination, IReadOnlyList<NotebookDocument> documents, int retention, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(destination); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, _prefix + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".moye");
        await backup.ExportAsync(path, documents, cancellationToken);
        // Only this library's completed automatic backups participate in retention.
        foreach (var expired in Directory.EnumerateFiles(directory, _prefix + "*.moye")
            .Where(candidate => !string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Math.Clamp(retention, 1, 100) - 1))
            File.Delete(expired);
        return path;
    }
}
