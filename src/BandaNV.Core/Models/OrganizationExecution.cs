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
    SkippedUnclassified,
    SkippedConflict,
    ConflictNeedsDecision,
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
    int? CategoryOrder);

public sealed class OrganizationExecutionRecord
{
    public string Format { get; set; } = "BandaNV.Execution.v2";
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "ORGANIZE";
    public OrganizationExecutionStatus Status { get; set; } =
        OrganizationExecutionStatus.Running;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;
    public string ConflictBehavior { get; set; } = string.Empty;

    public int EmptyDirectoriesDeleted { get; set; }
    public List<OrganizationExecutionItemRecord> Items { get; set; } = [];

    public int MovedCount =>
        Items.Count(item => item.Status == OrganizationExecutionItemStatus.Moved);

    public int IssueCount =>
        Items.Count(item =>
            item.Status is not OrganizationExecutionItemStatus.Moved and
            not OrganizationExecutionItemStatus.SkippedUnclassified);
}

public sealed class OrganizationExecutionItemRecord
{
    public string UndoId { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public string? FinalPath { get; set; }
    public string? ReplacedBackupPath { get; set; }

    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? CategoryOrder { get; set; }

    public long SizeBytes { get; set; }
    public long ModifiedUtcTicks { get; set; }

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
