using LumenvilLite.Models;
using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class BuildControlEndpoint
{
    public static void MapBuildControlEndpoint(this WebApplication app)
    {
        app.MapPost("/build/start", (BuildStartRequest request, BuildLauncher launcher, ProjectStore projects) =>
        {
            if (string.IsNullOrWhiteSpace(request.ProjectName))
            {
                return Results.BadRequest(new BuildStartResponse(
                    Started: false,
                    Build: null,
                    Error: "projectName is required.",
                    ErrorCode: "invalid_request"));
            }

            var project = projects.Get(request.ProjectName);
            if (project == null)
            {
                return Results.NotFound(new BuildStartResponse(
                    Started: false,
                    Build: null,
                    Error: $"Project '{request.ProjectName}' is not registered.",
                    ErrorCode: "project_not_found"));
            }

            var response = launcher.Start(request, project);
            if (response.Started)
            {
                return Results.Ok(response);
            }

            return response.ErrorCode switch
            {
                "build_in_progress" => Results.Conflict(response),
                "editor_open"       => Results.Conflict(response),
                "invalid_target"    => Results.BadRequest(response),
                "project_path_missing" => Results.BadRequest(response),
                "unsupported_platform" => Results.BadRequest(response),
                "unity_exe_missing" => Results.UnprocessableEntity(response),
                _                   => Results.Json(response, statusCode: 500)
            };
        });

        app.MapGet("/build/active", (BuildLauncher launcher) =>
            Results.Ok(new ActiveBuildResponse(launcher.GetActive())));

        app.MapPost("/build/cancel", (BuildLauncher launcher) =>
        {
            var response = launcher.Cancel();
            return response.Cancelled
                ? Results.Ok(response)
                : Results.BadRequest(response);
        });
    }
}
