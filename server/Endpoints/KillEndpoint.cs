using System.Diagnostics;
using LumenvilLite.Models;

namespace LumenvilLite.Endpoints;

public static class KillEndpoint
{
    private static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(5);
    private static readonly string[] AllowedProcessNames = { "Unity" };

    public static void MapKillEndpoint(this WebApplication app)
    {
        app.MapPost("/unity/{pid:int}/kill", async (int pid, KillRequest? body) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.BadRequest(new KillResponse(
                    Pid: pid,
                    Killed: false,
                    Method: "none",
                    ExitCode: null,
                    Error: "Kill is only supported on Windows hosts."));
            }

            Process? process;
            try
            {
                process = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return Results.NotFound(new KillResponse(
                    Pid: pid,
                    Killed: false,
                    Method: "none",
                    ExitCode: null,
                    Error: $"No process with pid {pid}."));
            }

            using (process)
            {
                if (!AllowedProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new KillResponse(
                        Pid: pid,
                        Killed: false,
                        Method: "none",
                        ExitCode: null,
                        Error: $"pid {pid} is '{process.ProcessName}', refusing to kill non-Unity process."));
                }

                var force = body?.Force == true;
                if (force)
                {
                    return ForceKill(process);
                }

                return await GracefulKillAsync(process);
            }
        });
    }

    private static async Task<IResult> GracefulKillAsync(Process process)
    {
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
            }
            else
            {
                // No main window means CloseMainWindow is a no-op; fall straight
                // through to force kill so the caller is not stuck waiting.
                return ForceKill(process, method: "force-no-window");
            }

            using var cts = new CancellationTokenSource(GracefulTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Graceful timed out — escalate.
                return ForceKill(process, method: "force-after-timeout");
            }

            return Results.Ok(new KillResponse(
                Pid: process.Id,
                Killed: true,
                Method: "graceful",
                ExitCode: SafeExitCode(process),
                Error: null));
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Graceful kill failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult ForceKill(Process process, string method = "force")
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
            return Results.Ok(new KillResponse(
                Pid: process.Id,
                Killed: true,
                Method: method,
                ExitCode: SafeExitCode(process),
                Error: null));
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Force kill failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }
}
