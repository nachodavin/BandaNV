namespace BandaNV.Core.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string SourceFolder { get; set; } = string.Empty;
    public string DestinationFolder { get; set; } = string.Empty;

    public string StartupPage { get; set; } = "Inicio";
    public string CloseBehavior { get; set; } = "Cerrar BandaNV";
    public bool StartWithWindows { get; set; }
    public bool AutoUpdate { get; set; } = true;

    public string Theme { get; set; } = "Oscuro";
    public string PrimaryColor { get; set; } = "#123A34";
    public string SecondaryColor { get; set; } = "#4FE0C6";

    public bool PreviewBeforeOrganize { get; set; } = true;
    public bool OrganizeFoldersAsUnits { get; set; } = true;
    public bool CreateFolders { get; set; } = true;
    public bool DeleteUnusedCategoryFolders { get; set; } = true;

    public string ConflictBehavior { get; set; } = "Preguntar";
    public string UnknownExtensionBehavior { get; set; } = "Preguntar en la vista previa";

    public bool UseRecycleBin { get; set; } = true;
    public bool ConfirmDestructiveActions { get; set; } = true;

    // Rutas protegidas por el usuario; la protección abarca todo su árbol
    // y también operaciones sobre carpetas superiores que lo afectarían.
    public List<string> ProtectedFolders { get; set; } = [];

    public bool SaveHistory { get; set; } = true;
    public bool SaveOrganizeHistory { get; set; } = true;
    public bool SaveSearchHistory { get; set; } = true;
    public string HistoryRetention { get; set; } = "Siempre";

    // Preferencias de visualización persistentes.
    // Filtros arrancan desactivados; orden y agrupación siguen el criterio
    // general definido para BandaNV.
    public string SearchDateFilter { get; set; } = "All";
    public DateTime? SearchSpecificDateFilter { get; set; }
    public string SearchSizeFilter { get; set; } = "All";
    public List<string> SearchExtensionFilters { get; set; } = [];
    public string SearchSortField { get; set; } = "DateModified";
    public string SearchSortDirection { get; set; } = "Descending";
    public string SearchGroupField { get; set; } = "DateModified";

    public string OrganizeDateFilter { get; set; } = "All";
    public DateTime? OrganizeSpecificDateFilter { get; set; }
    public string OrganizeSizeFilter { get; set; } = "All";
    public List<string> OrganizeExtensionFilters { get; set; } = [];
    public string OrganizeSortField { get; set; } = "DateModified";
    public string OrganizeSortDirection { get; set; } = "Descending";
    public string OrganizeGroupField { get; set; } = "DateModified";

    public string HistoryTypeFilter { get; set; } = "All";
    public string HistoryUndoFilter { get; set; } = "All";
    public string HistoryOriginFilter { get; set; } = string.Empty;
    public string HistorySortMode { get; set; } = "Newest";

    public List<CategorySettings> Categories { get; set; } = CreateDefaultCategories();

    // Metadato interno: permite reconocer con certeza carpetas que
    // pertenecieron a categorías eliminadas sin tocar carpetas ajenas.
    public List<string> OrphanedCategoryFolders { get; set; } = [];

    public static AppSettings CreateDefault() => new();

    private static List<CategorySettings> CreateDefaultCategories() =>
    [
        new("e23da338-8a04-4e80-9817-2d893289fd20", "RAR", [".7z", ".rar", ".zip"], 1, "#874ADE"),
        new("b22602bb-9ddb-4ab3-94a8-841c58a59122", "INSTALADORES", [".bat", ".exe", ".msi"], 2, "#59EBA9"),
        new("888c6178-64fd-41ae-b4bb-353c59ec6800", "DOCUMENTOS", [".docx", ".pdf", ".txt", ".xlsx"], 3, "#FFFFFF"),
        new("bdc43575-b4c8-4217-a8fb-2d82643a179f", "IMAGENES", [".avif", ".jpeg", ".jpg", ".png", ".webp"], 4, "#33B2EB"),
        new("3858f918-8138-461c-8e92-280859530182", "GIF", [".gif"], 5, "#33B2EB"),
        new("bb23b55f-d2a8-4ffd-bf77-56ed1228a257", "VIDEOS", [".avi", ".mkv", ".mov", ".mp4"], 6, "#FF0000"),
        new("56d8273d-789e-4f68-9ad4-3bb0c5345560", "AUDIO", [".aac", ".flac", ".mp3", ".ogg", ".wav"], 7, "#FF0000"),
        new("83c6e756-3a14-44dc-bbbc-ab5475cb338e", "EDITABLES", [".ai", ".eps", ".psd"], 8, "#FF7C35")
    ];
}

public sealed class CategorySettings
{
    public CategorySettings()
    {
    }

    public CategorySettings(
        string id,
        string name,
        IEnumerable<string> extensions,
        int order,
        string colorHex = "")
    {
        Id = id;
        Name = name;
        Extensions = extensions.ToList();
        Order = order;
        ColorHex = colorHex;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
    public int Order { get; set; }
    public string ColorHex { get; set; } = string.Empty;
}
