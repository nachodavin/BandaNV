namespace BandaNV.Core.Models;

public enum SearchFileActionStatus
{
    Completed,
    SkippedConflict,
    Missing,
    Error
}

public sealed record SearchFileActionItemResult(
    string SourcePath,
    string? FinalPath,
    SearchFileActionStatus Status,
    string? Message);

public sealed record SearchFileActionResult(
    IReadOnlyList<SearchFileActionItemResult> Items)
{
    public int CompletedCount =>
        Items.Count(item => item.Status == SearchFileActionStatus.Completed);

    public int IssueCount =>
        Items.Count - CompletedCount;
}
