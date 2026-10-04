namespace BandaNV.Core.Models;

public sealed record OrganizationAnalysisFile(
    string FullPath,
    string RelativePath,
    string FileName,
    string Extension,
    long SizeBytes,
    DateTime ModifiedAt,
    string? CategoryId,
    string? CategoryName,
    int? CategoryOrder,
    string? DestinationPath)
{
    public bool IsClassified =>
        CategoryOrder.HasValue &&
        !string.IsNullOrWhiteSpace(CategoryName);
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

    public long TotalSizeBytes =>
        Files.Sum(file => file.SizeBytes);
}
