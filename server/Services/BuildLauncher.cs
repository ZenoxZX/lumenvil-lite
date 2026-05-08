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
    // Unity's -executeMethod parser expects 'ClassName.MethodName' (it
    // refuses dotted namespace paths). The package class lives in
    // LumenvilLite.Editor.Build but we hand Unity only the leaf names.
    public const string DefaultExecuteMethod = "LumenvilLiteBuilder.Build";

    private readonly UnityProcessScanner _scanner;
    private readonly StepRunner _stepRunner;
    private readonly object _lock = new();

    public BuildLauncher(UnityProcessScanner scanner, StepRunner stepRunner)
    {
        _scanner = scanner;
        _stepRunner = stepRunner;
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

        var versionFile = Path.Combine(project.ProjectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionFile))
        {
            return new BuildStartResponse(
                Started: false,
                Build: null,
                Error: $"'{project.ProjectPath}' does not look like a Unity project (missing ProjectSettings/ProjectVersion.txt). " +
                       "Re-register the project from Manage Projects with the correct path.",
                ErrorCode: "project_invalid");
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

            IReadOnlyList<PreBuildStepResult> preBuildResults = Array.Empty<PreBuildStepResult>();
            if (request.RunPreBuildSteps && project.PreBuildSteps.Count > 0)
            {
                preBuildResults = _stepRunner.Run(project.PreBuildSteps, project.ProjectPath);
                var failure = preBuildResults.FirstOrDefault(r => r.ExitCode != 0);
                if (failure != null)
                {
                    // Persist a 'last build' record even though Unity was
                    // never spawned, so the editor window can show the git
                    // output retrospectively (and not just in the rejection
                    // dialog).
                    var nowUtc = DateTime.UtcNow;
                    var failedLast = new LastBuildInfo(
                        ProjectName: project.Name,
                        Target: request.Target,
                        Backend: request.Backend,
                        OutputPath: string.Empty,
                        LogFilePath: string.Empty,
                        Outcome: LastBuildOutcome.Failed,
                        ExitCode: failure.ExitCode,
                        StartedAtUtc: nowUtc,
                        FinishedAtUtc: nowUtc)
                    {
                        PreBuildResults = preBuildResults
                    };
                    WriteLastBuild(failedLast);

                    var stderr = string.IsNullOrWhiteSpace(failure.Stderr)
                        ? "(no stderr)"
                        : failure.Stderr.Trim();
                    return new BuildStartResponse(
                        Started: false,
                        Build: null,
                        Error: $"Pre-build step #{failure.StepIndex + 1} ({failure.Command}) failed with exit code {failure.ExitCode}: {stderr}",
                        ErrorCode: "prebuild_failed")
                    {
                        PreBuildResults = preBuildResults
                    };
                }
            }

            var response = LaunchUnchecked(request, project, preBuildResults);
            return response with { PreBuildResults = preBuildResults };
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

            var last = new LastBuildInfo(
                ProjectName: active.ProjectName,
                Target: active.Target,
                Backend: active.Backend,
                OutputPath: active.OutputPath,
                LogFilePath: active.LogFilePath,
                Outcome: LastBuildOutcome.Cancelled,
                ExitCode: -1,
                StartedAtUtc: active.StartedAtUtc,
                FinishedAtUtc: DateTime.UtcNow)
            {
                PreBuildResults = active.PreBuildResults ?? Array.Empty<PreBuildStepResult>()
            };
            WriteLastBuild(last);
            ClearState();
            return new BuildCancelResponse(true, null);
        }
    }

    [SupportedOSPlatform("windows")]
    private BuildStartResponse LaunchUnchecked(
        BuildStartRequest request,
        ProjectEntry project,
        IReadOnlyList<PreBuildStepResult> preBuildResults)
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

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var outputPath = Path.Combine(StoragePaths.BuildsRoot, project.Name, request.Target, timestamp);
        Directory.CreateDirectory(outputPath);

        var logFile = Path.Combine(StoragePaths.LogsDir, $"{project.Name}-{request.Target}-{timestamp}.log");

        var executeMethod = string.IsNullOrWhiteSpace(project.ExecuteMethod)
            ? DefaultExecuteMethod
            : project.ExecuteMethod.Trim();

        var args = new List<string>
        {
            "-batchmode",
            "-quit",
            "-nographics",
            "-projectPath", QuotePath(project.ProjectPath),
            "-buildTarget", request.Target,
            "-executeMethod", executeMethod,
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
        // Bare-flag style: only emit the arg when the flag is on. The builder
        // treats absence as false. Profiler / deep-profiling / script-debugging
        // depend on Development; we still send each one independently and let
        // the builder enforce the dependency, so a stale UI cannot silently
        // produce a release build with -allowDebugging set.
        if (request.Development) args.Add("-lumenvilDevelopment");
        if (request.AutoConnectProfiler) args.Add("-lumenvilAutoConnectProfiler");
        if (request.DeepProfiling) args.Add("-lumenvilDeepProfiling");
        if (request.ScriptDebugging) args.Add("-lumenvilScriptDebugging");

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
            StartedAtUtc: DateTime.UtcNow)
        {
            PreBuildResults = preBuildResults
        };

        WriteState(info);

        // Hook the process so the moment Unity exits we record the exit code
        // and convert the active build into a last-build record. Pulling
        // success / failure straight from the exit code is far more reliable
        // than scraping log lines.
        try
        {
            process.EnableRaisingEvents = true;
            var captured = info;
            process.Exited += (_, _) =>
            {
                // Process.ExitCode can throw "Process must exit before
                // requested information can be determined" when read too
                // soon after the event fires. WaitForExit() with no
                // timeout flushes Win32's bookkeeping so the exit code
                // is observable.
                int exitCode;
                try
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }

                var outcome = exitCode == 0 ? LastBuildOutcome.Success : LastBuildOutcome.Failed;

                // Post-build steps run after Unity exits regardless of outcome
                // so notify steps can announce failures too. They run outside
                // the main lock — they hit the network and disk and can take
                // minutes, blocking /status during that window would be rude.
                IReadOnlyList<PreBuildStepResult> postBuildResults = Array.Empty<PreBuildStepResult>();
                if (request.RunPostBuildSteps && project.PostBuildSteps.Count > 0)
                {
                    var env = new Dictionary<string, string>
                    {
                        ["LUMENVIL_PROJECT"]   = captured.ProjectName,
                        ["LUMENVIL_TARGET"]    = captured.Target,
                        ["LUMENVIL_OUTCOME"]   = outcome.ToString(),
                        ["LUMENVIL_EXIT_CODE"] = exitCode.ToString(),
                        ["LUMENVIL_OUTPUT"]    = captured.OutputPath
                    };

                    // The default builder writes PlayerSettings.bundleVersion
                    // to <output>/.lumenvil-version.txt; surface it as
                    // LUMENVIL_APP_VERSION when present so post-build steps
                    // can stamp uploads with the Unity Application.version.
                    try
                    {
                        var versionFile = Path.Combine(captured.OutputPath, ".lumenvil-version.txt");
                        if (File.Exists(versionFile))
                        {
                            var version = File.ReadAllText(versionFile).Trim();
                            if (!string.IsNullOrEmpty(version))
                            {
                                env["LUMENVIL_APP_VERSION"] = version;
                            }
                        }
                    }
                    catch
                    {
                        // Missing or unreadable — wrapper has its own fallback chain.
                    }
                    postBuildResults = _stepRunner.Run(project.PostBuildSteps, project.ProjectPath, env);
                }

                RecordCompletion(captured, exitCode, outcome, postBuildResults);
                process.Dispose();
            };
        }
        catch
        {
            // If we cannot wire the exit handler we fall back to log-based
            // detection — the active build will simply linger in state.json
            // until the next /status request notices the pid is gone.
        }

        return new BuildStartResponse(Started: true, Build: info, Error: null, ErrorCode: null);
    }

    private void RecordCompletion(
        ActiveBuildInfo active,
        int exitCode,
        LastBuildOutcome outcome,
        IReadOnlyList<PreBuildStepResult> postBuildResults)
    {
        lock (_lock)
        {
            // Only act if state.json still references this build; a cancel
            // request might have already cleared it.
            var current = ReadState();
            if (current == null || current.Pid != active.Pid)
            {
                return;
            }
            // Prefer the persisted state's pre-build results (they survived
            // a server restart); fall back to the captured copy if the
            // state file was reset.
            var preBuildResults = current.PreBuildResults?.Count > 0
                ? current.PreBuildResults
                : active.PreBuildResults;
            var last = new LastBuildInfo(
                ProjectName: active.ProjectName,
                Target: active.Target,
                Backend: active.Backend,
                OutputPath: active.OutputPath,
                LogFilePath: active.LogFilePath,
                Outcome: outcome,
                ExitCode: exitCode,
                StartedAtUtc: active.StartedAtUtc,
                FinishedAtUtc: DateTime.UtcNow)
            {
                PreBuildResults = preBuildResults ?? Array.Empty<PreBuildStepResult>(),
                PostBuildResults = postBuildResults ?? Array.Empty<PreBuildStepResult>()
            };
            WriteLastBuild(last);
            ClearState();
        }
    }

    public LastBuildInfo? GetLastBuild()
    {
        lock (_lock)
        {
            return ReadLastBuild();
        }
    }

    /// <summary>
    /// Returns the canonical build status for the UI: Building when there
    /// is an active spawn, otherwise the last completed run's outcome,
    /// otherwise Idle. The result is meant to override the regex
    /// classification done by UnityLogWatcher.
    /// </summary>
    public (BuildStatus status, string? logPath, LastBuildInfo? lastBuild, ActiveBuildInfo? active)
        GetCanonicalSnapshot()
    {
        lock (_lock)
        {
            var active = ReadStateChecked();
            if (active != null)
            {
                return (BuildStatus.Building, active.LogFilePath, ReadLastBuild(), active);
            }
            var last = ReadLastBuild();
            if (last != null)
            {
                var status = last.Outcome switch
                {
                    LastBuildOutcome.Success   => BuildStatus.Success,
                    LastBuildOutcome.Failed    => BuildStatus.Failed,
                    LastBuildOutcome.Cancelled => BuildStatus.Cancelled,
                    _                          => BuildStatus.Idle
                };
                return (status, last.LogFilePath, last, null);
            }
            return (BuildStatus.Idle, null, null, null);
        }
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
        // either way we no longer have an active build to report. Promote
        // it to a Failed last-build record so the UI does not silently
        // forget about the run.
        try
        {
            using var process = Process.GetProcessById(info.Pid);
            if (process.HasExited)
            {
                int exitCode;
                try
                {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }
                var outcome = exitCode == 0 ? LastBuildOutcome.Success : LastBuildOutcome.Failed;
                FinaliseFromPolling(info, exitCode, outcome);
                return null;
            }
            return info;
        }
        catch (ArgumentException)
        {
            // Process gone — we have no exit code, but the previous "find
            // by pid" path can disappear well after the build genuinely
            // finished, so default to Unknown rather than Failed and let
            // the user inspect the log.
            FinaliseFromPolling(info, -1, LastBuildOutcome.Unknown);
            return null;
        }
    }

    private void FinaliseFromPolling(ActiveBuildInfo active, int exitCode, LastBuildOutcome outcome)
    {
        var last = new LastBuildInfo(
            ProjectName: active.ProjectName,
            Target: active.Target,
            Backend: active.Backend,
            OutputPath: active.OutputPath,
            LogFilePath: active.LogFilePath,
            Outcome: outcome,
            ExitCode: exitCode,
            StartedAtUtc: active.StartedAtUtc,
            FinishedAtUtc: DateTime.UtcNow)
        {
            PreBuildResults = active.PreBuildResults ?? Array.Empty<PreBuildStepResult>()
        };
        WriteLastBuild(last);
        ClearState();
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

    private static LastBuildInfo? ReadLastBuild()
    {
        var path = StoragePaths.LastBuildFile;
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
            return JsonSerializer.Deserialize<LastBuildInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteLastBuild(LastBuildInfo info)
    {
        StoragePaths.EnsureRoot();
        var json = JsonSerializer.Serialize(info, JsonOptions);
        File.WriteAllText(StoragePaths.LastBuildFile, json);
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
        if (string.IsNullOrEmpty(version))
        {
            // No version means we cannot pick the right editor; refuse rather
            // than guess and accidentally launch a different Unity version.
            return null;
        }

        // Standard Unity Hub install roots, in order of likelihood.
        var hubRoots = new[]
        {
            @"C:\Program Files\Unity\Hub\Editor",
            @"C:\Program Files (x86)\Unity\Hub\Editor",
            @"D:\Program Files\Unity\Hub\Editor",
            @"D:\Unity\Hub\Editor"
        };

        foreach (var root in hubRoots)
        {
            var candidate = Path.Combine(root, version, "Editor", "Unity.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback: standalone install (rare).
        var standalone = $@"C:\Program Files\Unity\{version}\Editor\Unity.exe";
        return File.Exists(standalone) ? standalone : null;
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
