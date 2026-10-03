using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationPreviewItem>> PreviewAsync(
        string sourceFolder,
        IReadOnlyList<CategoryDefinition> categories,
        CancellationToken cancellationToken = default);

    Task<int> OrganizeAsync(
        IReadOnlyList<OrganizationPreviewItem> items,
        CancellationToken cancellationToken = default);
}
