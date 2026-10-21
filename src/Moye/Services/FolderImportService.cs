using System.IO;
using Moye.Localization;
using Moye.Models;
using L = Moye.Localization.LocalizationService;

namespace Moye.Services;

public enum FolderImportItemStatus { Ready, Failed, Skipped }

public sealed record FolderImportItem(string Name, FolderImportItemStatus Status, int PageCount = 0, string Message = "")
{
    private readonly string _message = Message;
    public LocalizedMessage? LocalizedMessage { get; init; }
    public string Message
    {
        get => LocalizedMessage?.ToString() ?? _message;
        init { _message = value; LocalizedMessage = null; }
    }
}

public sealed record FolderImportSummary(string NotebookTitle, IReadOnlyList<FolderImportItem> Items)
{
    public int ReadyCount => Items.Count(item => item.Status == FolderImportItemStatus.Ready);
    public int FailedCount => Items.Count(item => item.Status == FolderImportItemStatus.Failed);
    public int SkippedCount => Items.Count(item => item.Status == FolderImportItemStatus.Skipped);
    public int PageCount => Items.Where(item => item.Status == FolderImportItemStatus.Ready).Sum(item => item.PageCount);
    public bool RequiresConfirmation => ReadyCount > 0 && FailedCount > 0;
}

/// <summary>One-time, top-level import. Only page metadata accumulates; original assets are staged on disk.</summary>
public sealed class FolderImportService
{
    private readonly IOfficePdfConverter _converter;
    private readonly Func<INotebookRepository, IPdfService>? _pdfFactory;
    private readonly Func<string, INotebookRepository> _stagingRepositoryFactory;
    private readonly Func<string, FileAttributes> _readAttributes;

    public FolderImportService(IOfficePdfConverter? converter = null,
        Func<INotebookRepository, IPdfService>? pdfFactory = null,
        Func<string, INotebookRepository>? stagingRepositoryFactory = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _converter = converter ?? new OfficePdfConverter();
        _pdfFactory = pdfFactory;
        _stagingRepositoryFactory = stagingRepositoryFactory ?? (path => new SqliteNotebookRepository(path));
        _readAttributes = readAttributes ?? File.GetAttributes;
    }

    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".pdf" or ".pptx";

    public async Task<PreparedFolderImport> PrepareAsync(string folderPath,
        IProgress<DocumentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Path.GetFullPath(folderPath);
        progress?.Report(new(L.M("Messages.Progress.FindFolderDocuments")));
        var entries = await Task.Run(() => Discover(folder, cancellationToken), cancellationToken);
        var title = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var notebook = new NotebookDocument { Title = string.IsNullOrWhiteSpace(title) ? "Imported folder" : title, Folder = "My Notes" };
        var items = new List<FolderImportItem>();
        var temporary = Directory.CreateTempSubdirectory("PenroamFolderImport-").FullName;
        StagingRepository? staging = null;
        PreparedDocumentImport? prepared = null;
        try
        {
            try
            {
                staging = new StagingRepository(_stagingRepositoryFactory(Path.Combine(temporary, "staging.db")));
                await staging.InitializeAsync();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException and not FolderImportStagingException)
            { throw new FolderImportStagingException(ex); }
            prepared = new PreparedDocumentImport(staging, false, _converter, _pdfFactory);
            var total = entries.Count(entry => entry.SectionTitle is not null);
            var completed = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FailureReason is not null)
                {
                    items.Add(new(entry.Name, FolderImportItemStatus.Failed, Message: entry.FailureReason));
                    continue;
                }
                if (entry.SkipReason is not null)
                {
                    items.Add(new(entry.Name, FolderImportItemStatus.Skipped) { LocalizedMessage = entry.SkipReason });
                    continue;
                }
                progress?.Report(new(L.M("Messages.Progress.PrepareFolderFile", completed + 1, total, entry.Name), completed, total));
                IReadOnlyList<NotePage> pages;
                try
                {
                    var fileProgress = new FileProgress(progress, entry.Name, completed, total);
                    await prepared.PrepareAsync(entry.Path, fileProgress, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    pages = prepared.Pages;
                    if (pages.Count == 0) throw new InvalidDataException(L.Text("Messages.Folder.NoPages"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not FolderImportStagingException and not OutOfMemoryException)
                {
                    items.Add(new(entry.Name, FolderImportItemStatus.Failed, Message: ErrorReporter.Describe(ex)));
                    completed++;
                    continue;
                }
                // Keep each returned file group, not PreparedDocumentImport.Pages from the last file.
                var section = new NoteSection { Title = entry.SectionTitle! };
                notebook.Sections.Add(section);
                DocumentWorkflows.InsertPages(notebook, pages, section.Id);
                items.Add(new(entry.Name, FolderImportItemStatus.Ready, pages.Count) { LocalizedMessage = L.M("Messages.Folder.Section", section.Title) });
                completed++;
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(L.M("Messages.Progress.FolderReady"), total, total));
            cancellationToken.ThrowIfCancellationRequested();
            return new PreparedFolderImport(notebook, new(notebook.Title, items.ToArray()), prepared, staging, temporary);
        }
        catch
        {
            prepared?.Dispose();
            staging?.Dispose();
            PreparedFolderImport.RemoveTemporaryDirectory(temporary);
            throw;
        }
    }

    private IReadOnlyList<SourceEntry> Discover(string folder, CancellationToken token)
    {
        var entries = new List<SourceEntry>();
        // Enumerate once and never descend into directories, including directory junctions.
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            try
            {
                var isDirectory = (_readAttributes(path) & FileAttributes.Directory) != 0;
                var reason = isDirectory ? L.M("Messages.Folder.SubfolderSkipped")
                    : Path.GetExtension(path).Equals(".ppt", StringComparison.OrdinalIgnoreCase)
                        ? L.M("Messages.Folder.LegacyPowerPoint")
                    : !IsSupported(path) ? L.M("Messages.Folder.UnsupportedType") : null;
                entries.Add(new(path, name, null, reason));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One inaccessible or disappearing entry must not discard the other documents.
                // Failure to enumerate the selected folder itself still propagates.
                entries.Add(new(path, name, null, null, ErrorReporter.Describe(ex)));
            }
        }
        entries.Sort((left, right) => NaturalFileNameComparer.Instance.Compare(left.Name, right.Name));
        var reserved = entries.Where(entry => entry.SkipReason is null && entry.FailureReason is null)
            .Select(entry => SectionStem(entry.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].SkipReason is not null || entries[index].FailureReason is not null) continue;
            var stem = SectionStem(entries[index].Name);
            var sectionTitle = stem;
            for (var suffix = 2; !used.Add(sectionTitle); suffix++)
            {
                sectionTitle = $"{stem} ({suffix})";
                while (reserved.Contains(sectionTitle)) sectionTitle = $"{stem} ({++suffix})";
            }
            entries[index] = entries[index] with { SectionTitle = sectionTitle };
        }
        return entries;
    }

    private static string SectionStem(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(stem) ? "Untitled document" : stem;
    }

    private sealed record SourceEntry(string Path, string Name, string? SectionTitle, LocalizedMessage? SkipReason, string? FailureReason = null);

    private sealed class FileProgress(IProgress<DocumentProgress>? progress, string name, int completed, int total) : IProgress<DocumentProgress>
    {
        public void Report(DocumentProgress value) => progress?.Report(new(
            L.M("Messages.Folder.FileProgress", completed + 1, total, name, value.LocalizedMessage ?? (object)value.Message), completed, total));
    }

    // Storage failures are not bad source files. Never offer a silently incomplete batch when staging fails.
    private sealed class StagingRepository(INotebookRepository repository) : INotebookRepository
    {
        public async Task InitializeAsync()
        {
            try { await repository.InitializeAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            { throw new FolderImportStagingException(ex); }
        }
        public async Task<AssetData> PutAssetAsync(string fileName, string contentType, byte[] bytes)
        {
            try { return await repository.PutAssetAsync(fileName, contentType, bytes); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            { throw new FolderImportStagingException(ex); }
        }
        public async Task<AssetData> GetAssetAsync(string id)
        {
            try { return await repository.GetAssetAsync(id); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            { throw new FolderImportStagingException(ex); }
        }
        public Task<IReadOnlyList<NotebookSummary>> ListAsync() => repository.ListAsync();
        public Task<NotebookDocument?> LoadAsync(string id) => repository.LoadAsync(id);
        public Task SaveAsync(NotebookDocument document) => throw new NotSupportedException();
        public Task DeleteAsync(string id) => throw new NotSupportedException();
        public void Dispose() => repository.Dispose();
    }

    private sealed class NaturalFileNameComparer : IComparer<string>
    {
        public static NaturalFileNameComparer Instance { get; } = new();
        public int Compare(string? left, string? right)
        {
            if (left is null || right is null) return StringComparer.Ordinal.Compare(left, right);
            var a = 0; var b = 0;
            while (a < left.Length && b < right.Length)
            {
                if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
                {
                    var endA = a; var endB = b;
                    while (endA < left.Length && char.IsAsciiDigit(left[endA])) endA++;
                    while (endB < right.Length && char.IsAsciiDigit(right[endB])) endB++;
                    while (a < endA && left[a] == '0') a++;
                    while (b < endB && right[b] == '0') b++;
                    var compared = (endA - a).CompareTo(endB - b);
                    if (compared != 0) return compared;
                    compared = string.CompareOrdinal(left, a, right, b, endA - a);
                    if (compared != 0) return compared;
                    a = endA; b = endB;
                }
                else
                {
                    var compared = char.ToUpperInvariant(left[a]).CompareTo(char.ToUpperInvariant(right[b]));
                    if (compared != 0) return compared;
                    a++; b++;
                }
            }
            var remaining = (left.Length - a).CompareTo(right.Length - b);
            if (remaining != 0) return remaining;
            var tie = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return tie != 0 ? tie : StringComparer.Ordinal.Compare(left, right);
        }
    }
}

public sealed class FolderImportStagingException(Exception innerException) : IOException(
    L.Text("Messages.Folder.StagingFailed"), innerException) { }

/// <summary>A retry keeps the same notebook identity. Saving and opening are separate commit boundaries.</summary>
public sealed class PreparedFolderImport : IDisposable
{
    private readonly PreparedDocumentImport _prepared;
    private readonly INotebookRepository _staging;
    private readonly string _temporaryDirectory;
    private bool _accepted, _disposed;
    private int _saving;
    public NotebookDocument Notebook { get; }
    public FolderImportSummary Summary { get; }
    public bool HasSaved { get; private set; }

    internal PreparedFolderImport(NotebookDocument notebook, FolderImportSummary summary,
        PreparedDocumentImport prepared, INotebookRepository staging, string temporaryDirectory)
    {
        Notebook = notebook; Summary = summary; _prepared = prepared; _staging = staging;
        _temporaryDirectory = temporaryDirectory; _accepted = !summary.RequiresConfirmation;
    }

    public void AcceptSuccessfulFiles()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _accepted = true;
    }

    public async Task SaveAsync(INotebookRepository repository, Func<Task> savePendingChanges,
        IProgress<DocumentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HasSaved) return;
        if (Summary.ReadyCount == 0) throw new InvalidOperationException(L.Text("Messages.Folder.NothingPrepared"));
        if (!_accepted) throw new InvalidOperationException(L.Text("Messages.Folder.AcceptPartial"));
        if (Interlocked.Exchange(ref _saving, 1) != 0) throw new InvalidOperationException(L.Text("Messages.Folder.AlreadySaving"));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(L.M("Messages.Progress.SavePendingChanges")));
            await savePendingChanges();
            cancellationToken.ThrowIfCancellationRequested();
            await _prepared.CommitAssetsAsync(repository, Notebook.Pages, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(L.M("Messages.Progress.SaveImportedNotebook"), CanCancel: false));
            // Assets are content-addressed writes; only the notebook is one transaction.
            // A late cancellation or failed save can leave unreferenced library assets.
            await repository.SaveAsync(Notebook);
            HasSaved = true;
        }
        finally { Interlocked.Exchange(ref _saving, 0); }
    }

    public Task OpenSavedAsync(Func<string, Task> openNotebook)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HasSaved) throw new InvalidOperationException(L.Text("Messages.Folder.SaveBeforeOpen"));
        return openNotebook(Notebook.Id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prepared.Dispose();
        _staging.Dispose();
        RemoveTemporaryDirectory(_temporaryDirectory);
    }

    internal static void RemoveTemporaryDirectory(string path)
    {
        // Only this import's owned directory, after the repository has released its connections.
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
