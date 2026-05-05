using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class BuildEndpoint
{
    public static void MapBuildEndpoint(this WebApplication app)
    {
        app.MapGet("/build", (UnityLogWatcher watcher, UnityProcessScanner scanner, BuildLauncher launcher) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Ok(watcher.Inspect(candidateLogPath: null));
            }

            var snapshot = launcher.GetCanonicalSnapshot();
            DateTime? finishedAt = snapshot.lastBuild?.FinishedAtUtc;
            var candidateLogPath = snapshot.logPath;
            if (string.IsNullOrEmpty(candidateLogPath))
            {
                var unity = scanner.Scan();
                candidateLogPath = unity.Processes
                    .Select(p => p.LogFilePath)
                    .FirstOrDefault(path => !string.IsNullOrEmpty(path));
            }

            return Results.Ok(watcher.Inspect(candidateLogPath, snapshot.status, finishedAt));
        });
    }
}
