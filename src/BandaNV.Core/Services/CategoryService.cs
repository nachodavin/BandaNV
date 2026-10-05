using BandaNV.Core.Models;

namespace BandaNV.Core.Services;

public sealed class CategoryService
{
    private readonly SettingsService _settings;

    public CategoryService(SettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<CategorySettings> GetAll() =>
        _settings.Current.Categories
            .OrderBy(category => category.Order)
            .Select(Clone)
            .ToList();

    public Task SaveAllAsync(
        IEnumerable<CategorySettings> categories,
        CancellationToken cancellationToken = default) =>
        _settings.UpdateCategoriesAsync(categories, cancellationToken);

    public static string GetFolderName(int order, string name) =>
        $"{order} - {name.Trim()}";

    public static string GetFolderPath(
        string destinationRoot,
        int order,
        string name) =>
        Path.Combine(
            destinationRoot,
            GetFolderName(order, name));

    public static int CountExistingFiles(
        string destinationRoot,
        int order,
        string name)
    {
        try
        {
            var folder = GetFolderPath(destinationRoot, order, name);

            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(
                        folder,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Count()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static string? ResolveCurrentOrganizedFilePath(
        AppSettings settings,
        OrganizationExecutionRecord execution,
        OrganizationExecutionItemRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.FinalPath) &&
            File.Exists(item.FinalPath))
        {
            return item.FinalPath;
        }

        if (string.IsNullOrWhiteSpace(item.CategoryId))
        {
            return item.FinalPath;
        }

        var currentCategory = settings.Categories
            .FirstOrDefault(category =>
                category.Id.Equals(
                    item.CategoryId,
                    StringComparison.OrdinalIgnoreCase));

        if (currentCategory is null)
        {
            return item.FinalPath;
        }

        var fileName =
            !string.IsNullOrWhiteSpace(item.FinalPath)
                ? Path.GetFileName(item.FinalPath)
                : item.FileName;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return item.FinalPath;
        }

        return Path.Combine(
            GetFolderPath(
                execution.DestinationFolder,
                currentCategory.Order,
                currentCategory.Name),
            fileName);
    }

    private static CategorySettings Clone(CategorySettings category) =>
        new(
            category.Id,
            category.Name,
            category.Extensions,
            category.Order,
            category.ColorHex);
}
