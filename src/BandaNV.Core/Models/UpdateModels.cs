namespace BandaNV.Core.Models;

public enum UpdateCheckStatus
{
    Available,
    Current,
    LocalNewer,
    FailedSuppressed,
    Error
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string InstalledVersion,
    string AvailableVersion,
    string Tag,
    string ReleaseName,
    string ReleaseNotes,
    string ReleaseUrl,
    string AssetName,
    string AssetUrl,
    string Digest,
    string Message)
{
    public bool CanInstall =>
        Status == UpdateCheckStatus.Available &&
        !string.IsNullOrWhiteSpace(AssetUrl) &&
        !string.IsNullOrWhiteSpace(Digest);
}

public sealed record UpdateDownloadProgress(
    int Percentage,
    long BytesReceived,
    long? TotalBytes);

public sealed record PreparedUpdate(
    UpdateCheckResult Release,
    string WorkspaceRoot,
    string StagedApplicationDirectory,
    string UpdaterPath,
    string BackupDirectory,
    string ConfirmationPath,
    string Token);

public sealed class UpdatePackageManifest
{
    public const string CurrentFormat = "BandaNV.UpdateManifest.v1";

    public string Format { get; set; } = CurrentFormat;
    public string Version { get; set; } = string.Empty;
    public List<string> Files { get; set; } = [];
}
