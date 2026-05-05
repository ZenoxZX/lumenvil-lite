using LumenvilLite.Models;
using LumenvilLite.Services;

namespace LumenvilLite.Endpoints;

public static class ProjectsEndpoint
{
    public static void MapProjectsEndpoint(this WebApplication app)
    {
        app.MapGet("/projects", (ProjectStore store) =>
            Results.Ok(new ProjectListResponse(store.List())));

        app.MapPost("/projects", (ProjectEntry entry, ProjectStore store) =>
        {
            try
            {
                var added = store.Add(entry);
                return Results.Created($"/projects/{added.Name}", added);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPut("/projects/{name}", (string name, ProjectEntry entry, ProjectStore store) =>
        {
            try
            {
                var updated = store.Update(name, entry);
                if (updated == null)
                {
                    return Results.NotFound(new { error = $"Project '{name}' not found." });
                }
                return Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapDelete("/projects/{name}", (string name, ProjectStore store) =>
        {
            return store.Remove(name)
                ? Results.NoContent()
                : Results.NotFound(new { error = $"Project '{name}' not found." });
        });
    }
}
