namespace BandaNV.Core.Models;

public sealed record OrganizationPreviewItem(
    string SourcePath,
    string FileName,
    string Extension,
    string? Category,
    string? DestinationPath,
    bool IsClassified);
