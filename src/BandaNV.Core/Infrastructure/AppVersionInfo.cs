namespace BandaNV.Core.Infrastructure;

public static class AppVersionInfo
{
#if BANDANV_UPDATER_E2E_TARGET
    // Versión compilada exclusivamente para la prerelease de ensayo.
    public const string Version = "2.0.1";
    public const string Tag = "v2.0.1";
#else
    public const string Version = "2.0";
    public const string Tag = "v2.0";
#endif

    public const string GitHubRepository = "nachodavin/BandaNV";
    public const string GitHubApiBase =
        "https://api.github.com/repos/nachodavin/BandaNV";
    public const string GitHubReleasesUrl =
        "https://github.com/nachodavin/BandaNV/releases";
}
