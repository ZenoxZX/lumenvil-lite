using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class BuildEndpoint
{
    public static void MapBuildEndpoint(this WebApplication app)
    {
        app.MapGet("/build", (UnityLogWatcher watcher, UnityProcessScanner scanner, BuildLauncher launcher) =>
        {
            string? candidateLogPath = null;
            if (OperatingSystem.IsWindows())
            {
                var active = launcher.GetActive();
                if (active != null && !string.IsNullOrEmpty(active.LogFilePath))
                {
                    candidateLogPath = active.LogFilePath;
                }
                else
                {
                    var unity = scanner.Scan();
                    candidateLogPath = unity.Processes
                        .Select(p => p.LogFilePath)
                        .FirstOrDefault(path => !string.IsNullOrEmpty(path));
                }
            }

            return Results.Ok(watcher.Inspect(candidateLogPath));
        });
    }
}
