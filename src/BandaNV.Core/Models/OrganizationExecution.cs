namespace BandaNV.Core.Models;

public enum OrganizationExecutionStatus
{
    Running,
    Completed,
    CompletedWithIssues,
    Cancelled
}

public enum OrganizationExecutionItemStatus
{
    Planned,
    Moving,
    Moved,
    Renamed,
    Deleted,
    CompletedAction,
    SkippedUnclassified,
    SkippedConflict,
    ConflictNeedsDecision,
    Interrupted,
    SourceMissing,
    SourceChanged,
    Error
}

public sealed record OrganizationExecutionRequestItem(
    string FullPath,
    string FileName,
    long SizeBytes,
    long ModifiedUtcTicks,
    string? CategoryId,
    string? CategoryName,
    int? CategoryOrder,
    OrganizationAnalysisItemKind Kind = OrganizationAnalysisItemKind.File,
    int ContainedFileCount = 1,
    string? ContentFingerprint = null);

public sealed class OrganizationExecutionRecord
{
    public string Format { get; set; } = "BandaNV.Execution.v3";
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "ORGANIZE";
    public string? Action { get; set; }
    public OrganizationExecutionStatus Status { get; set; } =
        OrganizationExecutionStatus.Running;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string ConflictBehavior { get; set; } = string.Empty;
    public string? RelatedExecutionId { get; set; }
    public DateTime? RecoveredAt { get; set; }
    public string? RecoveryMessage { get; set; }

    public int EmptyDirectoriesDeleted { get; set; }
    public List<OrganizationExecutionItemRecord> Items { get; set; } = [];

    public int MovedCount =>
        Items.Count(item => item.Status == OrganizationExecutionItemStatus.Moved);

    public int SuccessfulCount =>
        Items.Count(item =>
            item.Status is OrganizationExecutionItemStatus.Moved or
                OrganizationExecutionItemStatus.Renamed or
                OrganizationExecutionItemStatus.Deleted or
                OrganizationExecutionItemStatus.CompletedAction);

    public int IssueCount =>
        Items.Count(item =>
            item.Status is not OrganizationExecutionItemStatus.Moved and
            not OrganizationExecutionItemStatus.Renamed and
            not OrganizationExecutionItemStatus.Deleted and
            not OrganizationExecutionItemStatus.CompletedAction and
            not OrganizationExecutionItemStatus.SkippedUnclassified);
}

public sealed class OrganizationExecutionItemRecord
{
    public string UndoId { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string? FinalPath { get; set; }
    public string? ReplacedBackupPath { get; set; }
    public string? ConflictResolution { get; set; }
    public long? ReplacedSizeBytes { get; set; }
    public long? ReplacedModifiedUtcTicks { get; set; }
    public OrganizationAnalysisItemKind? ReplacedKind { get; set; }
    public int? ReplacedContainedFileCount { get; set; }
    public string? ReplacedContentFingerprint { get; set; }
    public string? RelatedItemUndoId { get; set; }

    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? CategoryOrder { get; set; }

    public long SizeBytes { get; set; }
    public long ModifiedUtcTicks { get; set; }
    public OrganizationAnalysisItemKind Kind { get; set; } =
        OrganizationAnalysisItemKind.File;
    public int ContainedFileCount { get; set; } = 1;
    public string? ContentFingerprint { get; set; }

    public bool IsDirectory =>
        Kind == OrganizationAnalysisItemKind.Folder;

    public OrganizationExecutionItemStatus Status { get; set; } =
        OrganizationExecutionItemStatus.Planned;

    public string? Message { get; set; }
}

public sealed record OrganizationExecutionProgress(
    int Processed,
    int Total,
    string FileName,
    string Message);

public sealed record OrganizationExecutionResult(
    OrganizationExecutionRecord Record,
    string? HistoryPath,
    string? LogPath);
