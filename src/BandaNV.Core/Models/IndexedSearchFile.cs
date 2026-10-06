namespace BandaNV.Core.Models;

public sealed record IndexedSearchChild(
    string RelativePath,
    string Name,
    string Extension,
    long SizeBytes,
    DateTime ModifiedAt,
    bool IsDirectory = false,
    int ContainedFileCount = 1);

public sealed record IndexedSearchFile(
    string FullPath,
    string Name,
    string CategoryId,
    string CategoryName,
    int CategoryOrder,
    long SizeBytes,
    DateTime ModifiedAt,
    OrganizationAnalysisItemKind Kind = OrganizationAnalysisItemKind.File,
    int ContainedFileCount = 1,
    IReadOnlyList<IndexedSearchChild>? Contents = null)
{
    public bool IsDirectory =>
        Kind == OrganizationAnalysisItemKind.Folder;

    public IReadOnlyList<IndexedSearchChild> FolderContents =>
        Contents ?? [];
}
