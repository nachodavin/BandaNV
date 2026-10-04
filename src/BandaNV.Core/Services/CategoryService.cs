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

    private static CategorySettings Clone(CategorySettings category) =>
        new(
            category.Id,
            category.Name,
            category.Extensions,
            category.Order);
}
