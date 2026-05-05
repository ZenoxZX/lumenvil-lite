using LumenvilLite.Models;
using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class StatusEndpoint
{
    public static void MapStatusEndpoint(this WebApplication app)
    {
        app.MapGet("/status", (
            ServerInfo info,
            UnityProcessScanner scanner,
            UnityLogWatcher watcher,
            BuildLauncher launcher) =>
        {
            var uptime = (DateTime.UtcNow - info.StartedAtUtc).TotalSeconds;
            var health = new HealthResponse(
                Status: "ok",
                Name: "Lumenvil Lite",
                Version: "0.1.0",
                Hostname: Environment.MachineName,
                UptimeSeconds: Math.Round(uptime, 1));

            UnityResponse unity;
            string? candidateLogPath = null;
            ActiveBuildInfo? active = null;
            LastBuildInfo? lastBuild = null;
            BuildStatus? statusOverride = null;
            DateTime? finishedAtOverride = null;
            if (OperatingSystem.IsWindows())
            {
                unity = scanner.Scan();
                var snapshot = launcher.GetCanonicalSnapshot();
                active = snapshot.active;
                lastBuild = snapshot.lastBuild;
                statusOverride = snapshot.status;
                finishedAtOverride = lastBuild?.FinishedAtUtc;
                candidateLogPath = snapshot.logPath;
                if (string.IsNullOrEmpty(candidateLogPath))
                {
                    candidateLogPath = unity.Processes
                        .Select(p => p.LogFilePath)
                        .FirstOrDefault(path => !string.IsNullOrEmpty(path));
                }
            }
            else
            {
                unity = new UnityResponse(false, 0, Array.Empty<UnityProcessInfo>());
            }

            var build = watcher.Inspect(candidateLogPath, statusOverride, finishedAtOverride);

            return Results.Ok(new StatusResponse(health, unity, build, active, lastBuild));
        });
    }
}
