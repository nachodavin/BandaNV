namespace BandaNV.Core.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string SourceFolder { get; set; } = GetDefaultSourceFolder();
    public string DestinationFolder { get; set; } =
        Path.Combine(GetDefaultSourceFolder(), "ORGANIZADO");

    public string StartupPage { get; set; } = "Inicio";
    public string CloseBehavior { get; set; } = "Cerrar BandaNV";
    public bool StartWithWindows { get; set; }
    public bool AutoUpdate { get; set; } = true;

    public string Theme { get; set; } = "Oscuro";
    public string PrimaryColor { get; set; } = "#123A34";
    public string SecondaryColor { get; set; } = "#4FE0C6";

    public bool PreviewBeforeOrganize { get; set; } = true;
    public bool OrganizeFoldersAsUnits { get; set; }
    public bool CreateFolders { get; set; } = true;
    public bool DeleteEmptyFolders { get; set; }

    public string ConflictBehavior { get; set; } = "Preguntar";
    public string UnknownExtensionBehavior { get; set; } = "Preguntar en la vista previa";

    public bool UndoEnabled { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
    public bool ConfirmDestructiveActions { get; set; } = true;

    public bool SaveHistory { get; set; } = true;
    public bool SaveSearchHistory { get; set; } = true;
    public string HistoryRetention { get; set; } = "Siempre";

    public List<CategorySettings> Categories { get; set; } = CreateDefaultCategories();

    public static AppSettings CreateDefault() => new();

    private static string GetDefaultSourceFolder() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

    private static List<CategorySettings> CreateDefaultCategories() =>
    [
        new("e23da338-8a04-4e80-9817-2d893289fd20", "RAR", [".zip", ".rar", ".7z"], 1),
        new("b22602bb-9ddb-4ab3-94a8-841c58a59122", "INSTALLERS", [".exe", ".msi", ".bat"], 2),
        new("888c6178-64fd-41ae-b4bb-353c59ec6800", "DOCUMENTS", [".pdf", ".docx", ".xlsx", ".txt"], 3),
        new("bdc43575-b4c8-4217-a8fb-2d82643a179f", "IMAGES", [".jpg", ".jpeg", ".png", ".webp", ".avif"], 4),
        new("3858f918-8138-461c-8e92-280859530182", "GIF", [".gif"], 5),
        new("bb23b55f-d2a8-4ffd-bf77-56ed1228a257", "VIDEOS", [".mp4", ".mkv", ".mov", ".avi"], 6),
        new("56d8273d-789e-4f68-9ad4-3bb0c5345560", "AUDIO", [".mp3", ".wav", ".flac", ".aac", ".ogg"], 7),
        new("6de73ae0-6205-431f-8b6e-99ea743deacb", "FONTS", [".ttf", ".otf", ".woff", ".woff2"], 8),
        new("83c6e756-3a14-44dc-bbbc-ab5475cb338e", "DESIGN", [".ai", ".psd", ".indd", ".fig"], 9),
        new("fa573f41-f83d-4187-9bde-00a526c11971", "CODE", [".cs", ".ps1", ".js", ".json"], 10),
        new("2bbd45ab-373c-493a-9941-c56ce55cc4aa", "BACKUPS", [".bak", ".backup"], 11),
        new("65807868-819c-41b5-a879-40f4f79c12b0", "PROJECTS", [".sln", ".slnx", ".csproj"], 12),
        new("47a084a6-4bff-4389-95c0-9db56c213c10", "TEXTURES", [".tga", ".dds", ".exr"], 13),
        new("aecb244f-7586-4bfc-b19b-f1cadf78b8fb", "PACKAGES", [".nupkg", ".appx", ".msix"], 14)
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
