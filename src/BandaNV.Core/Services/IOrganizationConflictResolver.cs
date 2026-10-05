namespace BandaNV.Core.Services;

public enum OrganizationConflictAction
{
    Rename,
    Replace,
    Skip
}

public sealed record OrganizationConflictInfo(
    string FileName,
    string SourcePath,
    string DestinationPath,
    long SourceSizeBytes,
    long DestinationSizeBytes,
    DateTime SourceModifiedAt,
    DateTime DestinationModifiedAt);

public sealed record OrganizationConflictResolution(
    OrganizationConflictAction Action,
    bool ApplyToRemaining);

public interface IOrganizationConflictResolver
{
    Task<OrganizationConflictResolution> ResolveAsync(
        OrganizationConflictInfo conflict,
        CancellationToken cancellationToken = default);
}
