using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

public sealed class BuildLauncher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] AllowedTargets = { "StandaloneWindows64" };

    private readonly UnityProcessScanner _scanner;
    private readonly object _lock = new();

    public BuildLauncher(UnityProcessScanner scanner)
    {
        _scanner = scanner;
    }

    public ActiveBuildInfo? GetActive()
    {
        lock (_lock)
        {
            return ReadStateChecked();
        }
    }

    public BuildStartResponse Start(BuildStartRequest request, ProjectEntry project)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: "Builds can only be launched on a Windows host.",
                ErrorCode: "unsupported_platform");
        }

        if (!AllowedTargets.Contains(request.Target, StringComparer.OrdinalIgnoreCase))
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: $"Build target '{request.Target}' is not supported.",
                ErrorCode: "invalid_target");
        }

        if (!Directory.Exists(project.ProjectPath))
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: $"Project path does not exist: {project.ProjectPath}",
                ErrorCode: "project_path_missing");
        }

        lock (_lock)
        {
            var existing = ReadStateChecked();
            if (existing != null)
            {
                return new BuildStartResponse(
                    Started: false,
                    Build: existing,
                    Error: $"A build for '{existing.ProjectName}' is already running (pid {existing.Pid}).",
                    ErrorCode: "build_in_progress");
            }

            var lockingEditor = FindLockingEditor(project.ProjectPath);
            if (lockingEditor != null)
            {
                return new BuildStartResponse(
                    Started: false,
                    Build: null,
                    Error: $"Unity is already open on '{project.ProjectPath}' (pid {lockingEditor.Pid}). Quit it before starting a build.",
                    ErrorCode: "editor_open");
            }

            return LaunchUnchecked(request, project);
        }
    }

    public BuildCancelResponse Cancel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new BuildCancelResponse(false, "Cancel is only supported on Windows.");
        }

        lock (_lock)
        {
            var active = ReadStateChecked();
            if (active == null)
            {
                return new BuildCancelResponse(false, "No active build.");
            }

            try
            {
                using var process = Process.GetProcessById(active.Pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (ArgumentException)
            {
                // Already exited.
            }
            catch (Exception ex)
            {
                return new BuildCancelResponse(false, $"Failed to kill build process: {ex.Message}");
            }

            ClearState();
            return new BuildCancelResponse(true, null);
        }
    }

    [SupportedOSPlatform("windows")]
    private BuildStartResponse LaunchUnchecked(BuildStartRequest request, ProjectEntry project)
    {
        StoragePaths.EnsureRoot();
        Directory.CreateDirectory(StoragePaths.BuildsRoot);

        var unityExe = ResolveUnityExecutable(project.ProjectPath);
        if (unityExe == null)
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: "Could not locate Unity.exe. Open the project in the Hub once so its editor version is registered.",
                ErrorCode: "unity_exe_missing");
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var outputPath = Path.Combine(StoragePaths.BuildsRoot, project.Name, request.Target, timestamp);
        Directory.CreateDirectory(outputPath);

        var logFile = Path.Combine(StoragePaths.LogsDir, $"{project.Name}-{request.Target}-{timestamp}.log");

        var args = new List<string>
        {
            "-batchmode",
            "-quit",
            "-nographics",
            "-projectPath", QuotePath(project.ProjectPath),
            "-buildTarget", request.Target,
            "-executeMethod", project.ExecuteMethod,
            "-logFile", QuotePath(logFile),
            "-lumenvilTarget", request.Target,
            "-lumenvilBackend", request.Backend.ToString(),
            "-lumenvilOutput", QuotePath(outputPath)
        };
        if (!string.IsNullOrWhiteSpace(request.Defines))
        {
            args.Add("-lumenvilDefines");
            args.Add(QuotePath(request.Defines));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = unityExe,
            Arguments = string.Join(' ', args),
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = project.ProjectPath
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: $"Failed to start Unity: {ex.Message}",
                ErrorCode: "spawn_failed");
        }

        if (process == null)
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: "Process.Start returned null.",
                ErrorCode: "spawn_failed");
        }

        var info = new ActiveBuildInfo(
            ProjectName: project.Name,
            Target: request.Target,
            Backend: request.Backend,
            OutputPath: outputPath,
            LogFilePath: logFile,
            Pid: process.Id,
            StartedAtUtc: DateTime.UtcNow);

        WriteState(info);
        return new BuildStartResponse(Started: true, Build: info, Error: null, ErrorCode: null);
    }

    [SupportedOSPlatform("windows")]
    private UnityProcessInfo? FindLockingEditor(string projectPath)
    {
        var unity = _scanner.Scan();
        var normalized = NormalisePath(projectPath);
        return unity.Processes.FirstOrDefault(p =>
            p.Type == UnityProcessType.Editor &&
            !string.IsNullOrEmpty(p.ProjectPath) &&
            string.Equals(NormalisePath(p.ProjectPath), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private ActiveBuildInfo? ReadStateChecked()
    {
        var info = ReadState();
        if (info == null)
        {
            return null;
        }
        if (!OperatingSystem.IsWindows())
        {
            return info;
        }
        // If the recorded process is gone, the build finished or crashed —
        // either way we no longer have an active build to report.
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            if (process.HasExited)
            {
                ClearState();
                return null;
            }
            return info;
        }
        catch (ArgumentException)
        {
            ClearState();
            return null;
        }
    }

    private static ActiveBuildInfo? ReadState()
    {
        var path = StoragePaths.StateFile;
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return JsonSerializer.Deserialize<ActiveBuildInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(ActiveBuildInfo info)
    {
        StoragePaths.EnsureRoot();
        var json = JsonSerializer.Serialize(info, JsonOptions);
        File.WriteAllText(StoragePaths.StateFile, json);
    }

    private static void ClearState()
    {
        var path = StoragePaths.StateFile;
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* swallow */ }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ResolveUnityExecutable(string projectPath)
    {
        // Strategy: read ProjectSettings/ProjectVersion.txt to find the editor
        // version, then look it up in the standard Unity Hub install root.
        var versionFile = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
        {
            return ProbeStandardLocations(null);
        }

        string? version = null;
        foreach (var line in File.ReadAllLines(versionFile))
        {
            if (line.StartsWith("m_EditorVersion:", StringComparison.OrdinalIgnoreCase))
            {
                version = line.Substring("m_EditorVersion:".Length).Trim();
                break;
            }
        }
        return ProbeStandardLocations(version);
    }

    [SupportedOSPlatform("windows")]
    private static string? ProbeStandardLocations(string? version)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(version))
        {
            candidates.Add($@"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe");
            candidates.Add($@"C:\Program Files\Unity\{version}\Editor\Unity.exe");
        }
        candidates.Add(@"C:\Program Files\Unity\Hub\Editor\Unity.exe");
        candidates.Add(@"C:\Program Files\Unity\Editor\Unity.exe");

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalisePath(string path)
    {
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    private static string QuotePath(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.Contains(' ') || value.Contains('\t'))
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }
        return value;
    }
}
