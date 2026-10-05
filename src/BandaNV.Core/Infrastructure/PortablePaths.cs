namespace BandaNV.Core.Infrastructure;

public static class PortablePaths
{
    public static string RootDirectory { get; } =
        Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static string ConfigDirectory =>
        Path.Combine(RootDirectory, "config");

    public static string LogsDirectory =>
        Path.Combine(RootDirectory, "logs");

    public static string HistoryDirectory =>
        Path.Combine(RootDirectory, "history");

    public static string SettingsFile =>
        Path.Combine(ConfigDirectory, "bandanv_settings.json");

    public static string UpdateStateFile =>
        Path.Combine(ConfigDirectory, "bandanv_update_state.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
    }
}
