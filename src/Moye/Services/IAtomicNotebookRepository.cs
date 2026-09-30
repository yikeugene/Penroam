using Moye.Models;

namespace Moye.Services;

/// <summary>Commits a cross-notebook operation as one transaction, or leaves every notebook unchanged.</summary>
public interface IAtomicNotebookRepository
{
    Task SaveBatchAsync(IReadOnlyList<NotebookDocument> documents);
}
