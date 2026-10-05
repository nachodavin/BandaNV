namespace BandaNV.Core.Models;

public sealed record CategoryFolderSyncResult(
    bool Success,
    int RenamedFolders,
    int CreatedFolders,
    int DeletedEmptyFolders,
    int PreservedDeletedFolders,
    bool Deferred,
    string? ErrorMessage)
{
    public static CategoryFolderSyncResult NoChanges() =>
        new(true, 0, 0, 0, 0, false, null);

    public static CategoryFolderSyncResult DeferredSync() =>
        new(true, 0, 0, 0, 0, true, null);

    public static CategoryFolderSyncResult Failed(string message) =>
        new(false, 0, 0, 0, 0, false, message);
}
