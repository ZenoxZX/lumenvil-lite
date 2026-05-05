using LumenvilLite.Models;

namespace LumenvilLite.Endpoints;

public static class HealthEndpoint
{
    public static void MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", (ServerInfo info) =>
        {
            var uptime = (DateTime.UtcNow - info.StartedAtUtc).TotalSeconds;
            return Results.Ok(new HealthResponse(
                Status: "ok",
                Name: "Lumenvil Lite",
                Version: "0.1.0",
                Hostname: Environment.MachineName,
                UptimeSeconds: Math.Round(uptime, 1)));
        });
    }
}
