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
            if (OperatingSystem.IsWindows())
            {
                unity = scanner.Scan();
                // Prefer the active build's log so a fresh batch build is
                // surfaced even before its Unity.exe shows up in the scan,
                // and so the right log gets tailed when both an interactive
                // editor and a batch build are open at once.
                active = launcher.GetActive();
                if (active != null && !string.IsNullOrEmpty(active.LogFilePath))
                {
                    candidateLogPath = active.LogFilePath;
                }
                else
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

            var build = watcher.Inspect(candidateLogPath);

            return Results.Ok(new StatusResponse(health, unity, build, active));
        });
    }
}
