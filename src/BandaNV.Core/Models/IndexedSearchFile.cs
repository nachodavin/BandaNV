namespace BandaNV.Core.Models;

public sealed record IndexedSearchFile(
    string FullPath,
    string Name,
    string CategoryId,
    string CategoryName,
    int CategoryOrder,
    long SizeBytes,
    DateTime ModifiedAt);
