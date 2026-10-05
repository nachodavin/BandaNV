namespace BandaNV.Core.Models;

public enum OrganizationAnalysisItemKind
{
    File,
    Folder
}

public sealed record OrganizationAnalysisFolderFile(
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTime ModifiedAt,
    string? CategoryId,
    string? CategoryName,
    int? CategoryOrder)
{
    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);
}

public sealed record OrganizationAnalysisFile(
    string FullPath,
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTime ModifiedAt,
    long ModifiedUtcTicks,
    string? CategoryId,
    string? CategoryName,
    int? CategoryOrder,
    string? DestinationPath,
    bool HasDestinationConflict,
    OrganizationAnalysisItemKind Kind = OrganizationAnalysisItemKind.File,
    int ContainedFileCount = 1,
    int RecognizedFileCount = 0,
    int DistinctCategoryCount = 0,
    bool ScanIncomplete = false,
    IReadOnlyList<OrganizationAnalysisFolderFile>? FolderFiles = null)
{
    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);

    public bool IsDirectory =>
        Kind == OrganizationAnalysisItemKind.Folder;

    public IReadOnlyList<OrganizationAnalysisFolderFile> FolderContents =>
        FolderFiles ?? [];
}

public sealed record OrganizationAnalysisResult(
    string SourceFolder,
    string DestinationFolder,
    IReadOnlyList<OrganizationAnalysisFile> Files,
    int SkippedDirectories)
{
    public int ClassifiedCount =>
        Files.Count(file => file.IsClassified);

    public int UnclassifiedCount =>
        Files.Count - ClassifiedCount;

    public int FolderCount =>
        Files.Count(file => file.IsDirectory);

    public long TotalSizeBytes =>
        Files.Sum(file => file.SizeBytes);
}
