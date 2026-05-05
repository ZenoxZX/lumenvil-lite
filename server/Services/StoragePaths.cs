namespace LumenvilLite.Services;

public static class StoragePaths
{
    private static readonly string OverrideRoot =
        Environment.GetEnvironmentVariable("LUMENVIL_LITE_DATA_DIR") ?? string.Empty;

    public static string Root
    {
        get
        {
            if (!string.IsNullOrEmpty(OverrideRoot))
            {
                return OverrideRoot;
            }
            if (OperatingSystem.IsWindows())
            {
                return @"C:\Tools\LumenvilLite";
            }
            // Dev fallback when the server runs from a non-Windows host.
            return Path.Combine(Path.GetTempPath(), "LumenvilLite");
        }
    }

    public static string ProjectsFile => Path.Combine(Root, "projects.json");
    public static string StateFile    => Path.Combine(Root, "state.json");
    public static string LogsDir      => Path.Combine(Root, "logs");
    public static string BuildsRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return @"C:\Builds";
            }
            return Path.Combine(Path.GetTempPath(), "LumenvilBuilds");
        }
    }

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDir);
    }
}
