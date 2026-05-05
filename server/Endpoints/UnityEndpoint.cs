using LumenvilLite.Models;
using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class UnityEndpoint
{
    public static void MapUnityEndpoint(this WebApplication app)
    {
        app.MapGet("/unity", (UnityProcessScanner scanner) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.Ok(new UnityResponse(false, 0, Array.Empty<UnityProcessInfo>()));
            }

            return Results.Ok(scanner.Scan());
        });
    }
}
