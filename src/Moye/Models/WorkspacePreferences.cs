namespace Moye.Models;

public sealed record ReadingPosition(string SectionId, string PageId, double X, double Y,
    double ViewX, double ViewY, double Zoom, bool FitWidth, DateTimeOffset ViewedUtc);

/// <summary>Device-local navigation and safety preferences, never part of notebook undo.</summary>
public sealed class WorkspacePreferences
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ReadingPosition> ReadingPositions { get; set; } = [];
    public string LastNotebookId { get; set; } = "";
    public bool TwoFingerNavigationOnly { get; set; }
    public bool LockZoom { get; set; }
    public bool CompactToolbar { get; set; }
    public bool FocusToolsOnRight { get; set; }
    public bool BackupEnabled { get; set; }
    public string BackupDirectory { get; set; } = "";
    public int BackupIntervalHours { get; set; } = 24;
    public int BackupRetention { get; set; } = 10;
    public DateTimeOffset? LastBackupUtc { get; set; }
    public string LastBackupError { get; set; } = "";
}
