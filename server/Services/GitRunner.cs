using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

/// <summary>
/// Executes a list of <see cref="StepDefinition"/>s in order. Each step
/// belongs to one of three preset groups (git, filesystem, notify) or is
/// a custom shell command run through bash / cmd / pwsh / direct.
/// </summary>
public sealed class StepRunner
{
    private const int PerStepTimeoutMs = 5 * 60 * 1000; // 5 minutes per step.

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Runs the steps in order. Returns a list of results. If any step
    /// produces a non-zero exit code the chain stops at that step;
    /// successful results before it are still in the list. Environment
    /// variables in <paramref name="extraEnv"/> are exposed to custom
    /// shell steps and notify steps so post-build chains can read the
    /// build outcome.
    /// </summary>
    public IReadOnlyList<PreBuildStepResult> Run(
        IReadOnlyList<StepDefinition> steps,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var results = new List<PreBuildStepResult>();
        if (steps == null || steps.Count == 0)
        {
            return results;
        }
        if (!Directory.Exists(workingDirectory))
        {
            results.Add(new PreBuildStepResult(
                StepIndex: 0,
                Command: "(precheck)",
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: $"Working directory does not exist: {workingDirectory}"));
            return results;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var result = ExecuteStep(step, workingDirectory, i, extraEnv);
            results.Add(result);
            if (result.ExitCode != 0)
            {
                break; // Stop on the first failure.
            }
        }
        return results;
    }

    private static PreBuildStepResult ExecuteStep(
        StepDefinition step,
        string workingDirectory,
        int stepIndex,
        IReadOnlyDictionary<string, string>? extraEnv)
    {
        var kind = (step.Kind ?? "preset").Trim().ToLowerInvariant();
        if (kind == "custom")
        {
            return RunCustom(step, workingDirectory, stepIndex, extraEnv);
        }

        var group = (step.Group ?? "git").Trim().ToLowerInvariant();
        return group switch
        {
            "git"        => RunGitPreset(step, workingDirectory, stepIndex),
            "filesystem" => RunFilesystemPreset(step, workingDirectory, stepIndex),
            "notify"     => RunNotifyPreset(step, workingDirectory, stepIndex, extraEnv),
            _ => new PreBuildStepResult(
                StepIndex: stepIndex,
                Command: $"(unknown group '{group}')",
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: $"Unknown preset group '{group}'.")
        };
    }

    // ---- Git preset --------------------------------------------------------

    private static PreBuildStepResult RunGitPreset(StepDefinition step, string workingDirectory, int stepIndex)
    {
        var args = BuildGitArgs(step);
        var commandPreview = "git " + JoinForDisplay(args);
        return RunProcess("git", args, workingDirectory, stepIndex, commandPreview, extraEnv: null);
    }

    private static IReadOnlyList<string> BuildGitArgs(StepDefinition step)
    {
        var subset = (step.Subset ?? string.Empty).Trim().ToLowerInvariant();
        var extra = ParseCommandLine(step.Args ?? string.Empty);

        switch (subset)
        {
            case "fetch":    return Combine("fetch", extra);
            case "pull":     return Combine("pull", extra);
            case "checkout": return Combine("checkout", extra);
            case "restore":  return Combine("restore", extra.Count == 0 ? new[] { "." } : extra);
            case "reset":    return Combine("reset", extra.Count == 0 ? new[] { "--hard" } : extra);
            case "status":   return Combine("status", extra);
            case "clean":    return Combine("clean", extra.Count == 0 ? new[] { "-fd" } : extra);
            case "tag":      return Combine("tag", extra);
            default:         return Combine("status", Array.Empty<string>());
        }
    }

    // ---- Filesystem preset -------------------------------------------------

    private static PreBuildStepResult RunFilesystemPreset(StepDefinition step, string workingDirectory, int stepIndex)
    {
        var subset = (step.Subset ?? string.Empty).Trim().ToLowerInvariant();
        var args = ParseCommandLine(step.Args ?? string.Empty);
        var preview = $"filesystem {subset} " + JoinForDisplay(args);

        try
        {
            switch (subset)
            {
                case "copy":   return DoCopy(args, workingDirectory, stepIndex, preview);
                case "move":   return DoMove(args, workingDirectory, stepIndex, preview);
                case "delete": return DoDelete(args, workingDirectory, stepIndex, preview);
                case "mkdir":  return DoMkdir(args, workingDirectory, stepIndex, preview);
                case "zip":    return DoZip(args, workingDirectory, stepIndex, preview);
                default:
                    return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                        $"Unknown filesystem subset '{subset}'.");
            }
        }
        catch (Exception ex)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty, ex.Message);
        }
    }

    private static PreBuildStepResult DoCopy(IReadOnlyList<string> args, string cwd, int stepIndex, string preview)
    {
        if (args.Count < 2)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "copy requires <src> <dst>");
        }
        var src = ResolvePath(args[0], cwd);
        var dst = ResolvePath(args[1], cwd);

        if (Directory.Exists(src))
        {
            CopyDirectoryRecursive(src, dst);
            return new PreBuildStepResult(stepIndex, preview, 0,
                $"Copied directory {src} -> {dst}", string.Empty);
        }
        if (File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? cwd);
            File.Copy(src, dst, overwrite: true);
            return new PreBuildStepResult(stepIndex, preview, 0,
                $"Copied file {src} -> {dst}", string.Empty);
        }
        return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
            $"Source not found: {src}");
    }

    private static PreBuildStepResult DoMove(IReadOnlyList<string> args, string cwd, int stepIndex, string preview)
    {
        if (args.Count < 2)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "move requires <src> <dst>");
        }
        var src = ResolvePath(args[0], cwd);
        var dst = ResolvePath(args[1], cwd);
        if (Directory.Exists(src))
        {
            Directory.Move(src, dst);
        }
        else if (File.Exists(src))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? cwd);
            File.Move(src, dst, overwrite: true);
        }
        else
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                $"Source not found: {src}");
        }
        return new PreBuildStepResult(stepIndex, preview, 0, $"Moved {src} -> {dst}", string.Empty);
    }

    private static PreBuildStepResult DoDelete(IReadOnlyList<string> args, string cwd, int stepIndex, string preview)
    {
        if (args.Count < 1)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "delete requires <path> (or more)");
        }
        var sb = new StringBuilder();
        foreach (var rel in args)
        {
            var p = ResolvePath(rel, cwd);
            if (Directory.Exists(p))
            {
                Directory.Delete(p, recursive: true);
                sb.AppendLine($"Deleted directory {p}");
            }
            else if (File.Exists(p))
            {
                File.Delete(p);
                sb.AppendLine($"Deleted file {p}");
            }
            else
            {
                sb.AppendLine($"Skipped (not found): {p}");
            }
        }
        return new PreBuildStepResult(stepIndex, preview, 0, sb.ToString().TrimEnd(), string.Empty);
    }

    private static PreBuildStepResult DoMkdir(IReadOnlyList<string> args, string cwd, int stepIndex, string preview)
    {
        if (args.Count < 1)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "mkdir requires <path> (or more)");
        }
        var sb = new StringBuilder();
        foreach (var rel in args)
        {
            var p = ResolvePath(rel, cwd);
            Directory.CreateDirectory(p);
            sb.AppendLine($"Created {p}");
        }
        return new PreBuildStepResult(stepIndex, preview, 0, sb.ToString().TrimEnd(), string.Empty);
    }

    private static PreBuildStepResult DoZip(IReadOnlyList<string> args, string cwd, int stepIndex, string preview)
    {
        if (args.Count < 2)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "zip requires <src-dir> <dst-zip>");
        }
        var src = ResolvePath(args[0], cwd);
        var dst = ResolvePath(args[1], cwd);
        if (!Directory.Exists(src))
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                $"Source directory not found: {src}");
        }
        if (File.Exists(dst)) File.Delete(dst);
        Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? cwd);
        ZipFile.CreateFromDirectory(src, dst, CompressionLevel.Optimal, includeBaseDirectory: false);
        var size = new FileInfo(dst).Length;
        return new PreBuildStepResult(stepIndex, preview, 0,
            $"Zipped {src} -> {dst} ({size:N0} bytes)", string.Empty);
    }

    // ---- Notify preset -----------------------------------------------------

    private static PreBuildStepResult RunNotifyPreset(
        StepDefinition step,
        string workingDirectory,
        int stepIndex,
        IReadOnlyDictionary<string, string>? extraEnv)
    {
        var subset = (step.Subset ?? string.Empty).Trim().ToLowerInvariant();
        var args = ParseCommandLine(step.Args ?? string.Empty);
        var preview = $"notify {subset} " + JoinForDisplay(MaskUrl(args));

        try
        {
            return subset switch
            {
                "slack"    => DoWebhook(args, stepIndex, preview, BuildSlackBody(extraEnv)),
                "discord"  => DoWebhook(args, stepIndex, preview, BuildDiscordBody(extraEnv)),
                "httppost" => DoHttpPost(args, stepIndex, preview),
                _ => new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                    $"Unknown notify subset '{subset}'.")
            };
        }
        catch (Exception ex)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty, ex.Message);
        }
    }

    private static PreBuildStepResult DoWebhook(IReadOnlyList<string> args, int stepIndex, string preview, string jsonBody)
    {
        if (args.Count < 1)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "webhook requires <url>");
        }
        var url = args[0];
        return PostJson(url, jsonBody, stepIndex, preview);
    }

    private static PreBuildStepResult DoHttpPost(IReadOnlyList<string> args, int stepIndex, string preview)
    {
        if (args.Count < 1)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty,
                "httpPost requires <url> [<json-body>]");
        }
        var url = args[0];
        var body = args.Count >= 2 ? args[1] : "{}";
        return PostJson(url, body, stepIndex, preview);
    }

    private static PreBuildStepResult PostJson(string url, string jsonBody, int stepIndex, string preview)
    {
        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = Http.PostAsync(url, content).GetAwaiter().GetResult();
            var respBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var truncated = respBody.Length > 500 ? respBody.Substring(0, 500) + "…" : respBody;
            var stdout = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{truncated}";
            return response.IsSuccessStatusCode
                ? new PreBuildStepResult(stepIndex, preview, 0, stdout, string.Empty)
                : new PreBuildStepResult(stepIndex, preview, (int)response.StatusCode, stdout, "non-2xx response");
        }
        catch (Exception ex)
        {
            return new PreBuildStepResult(stepIndex, preview, -1, string.Empty, ex.Message);
        }
    }

    private static string BuildSlackBody(IReadOnlyDictionary<string, string>? env)
    {
        var text = ComposeNotifyText(env);
        return JsonSerializer.Serialize(new { text });
    }

    private static string BuildDiscordBody(IReadOnlyDictionary<string, string>? env)
    {
        var content = ComposeNotifyText(env);
        return JsonSerializer.Serialize(new { content });
    }

    private static string ComposeNotifyText(IReadOnlyDictionary<string, string>? env)
    {
        if (env == null || env.Count == 0)
        {
            return "Lumenvil Lite: pre-build notify (no build context).";
        }
        env.TryGetValue("LUMENVIL_PROJECT", out var project);
        env.TryGetValue("LUMENVIL_TARGET", out var target);
        env.TryGetValue("LUMENVIL_OUTCOME", out var outcome);
        env.TryGetValue("LUMENVIL_EXIT_CODE", out var exitCode);
        return $"Build *{project ?? "(?)"}* / {target ?? "(?)"} → *{outcome ?? "(?)"}* (exit {exitCode ?? "?"})";
    }

    private static IReadOnlyList<string> MaskUrl(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return args;
        var copy = new List<string>(args);
        var first = copy[0];
        if (first.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var idx = first.IndexOf("://", StringComparison.Ordinal);
            if (idx > 0 && first.Length > idx + 12)
            {
                copy[0] = first.Substring(0, idx + 3) + "…" + first[^6..];
            }
        }
        return copy;
    }

    // ---- Custom path -------------------------------------------------------

    private static PreBuildStepResult RunCustom(
        StepDefinition step,
        string workingDirectory,
        int stepIndex,
        IReadOnlyDictionary<string, string>? extraEnv)
    {
        var interpreter = (step.Interpreter ?? "bash").Trim().ToLowerInvariant();
        var command = step.Command ?? string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return new PreBuildStepResult(stepIndex, $"({interpreter}) (empty)", -1, string.Empty,
                "Custom step has no command.");
        }

        switch (interpreter)
        {
            case "bash":
            {
                var bash = ResolveBashPath();
                if (bash == null)
                {
                    return new PreBuildStepResult(stepIndex, $"bash -c {command}", -2, string.Empty,
                        "bash interpreter not found (looked for Git for Windows bash.exe and bash on PATH).");
                }
                return RunProcess(bash, new[] { "-c", command }, workingDirectory, stepIndex,
                    $"bash -c {command}", extraEnv);
            }
            case "cmd":
                return RunProcess("cmd.exe", new[] { "/c", command }, workingDirectory, stepIndex,
                    $"cmd /c {command}", extraEnv);
            case "pwsh":
                return RunProcess("powershell.exe", new[] { "-NoProfile", "-Command", command },
                    workingDirectory, stepIndex, $"pwsh -Command {command}", extraEnv);
            case "direct":
            {
                var parsed = ParseCommandLine(command);
                if (parsed.Count == 0)
                {
                    return new PreBuildStepResult(stepIndex, command, -1, string.Empty,
                        "direct: no executable.");
                }
                var exe = parsed[0];
                var rest = parsed.Skip(1).ToList();
                return RunProcess(exe, rest, workingDirectory, stepIndex, command, extraEnv);
            }
            default:
                return new PreBuildStepResult(stepIndex, command, -1, string.Empty,
                    $"Unknown interpreter '{interpreter}'. Use bash | cmd | pwsh | direct.");
        }
    }

    private static string? ResolveBashPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                @"C:\Program Files\Git\bin\bash.exe",
                @"C:\Program Files (x86)\Git\bin\bash.exe"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
        }
        // Otherwise rely on PATH.
        return "bash";
    }

    // ---- Process plumbing --------------------------------------------------

    private static PreBuildStepResult RunProcess(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        int stepIndex,
        string commandPreview,
        IReadOnlyDictionary<string, string>? extraEnv)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new PreBuildStepResult(stepIndex, commandPreview, -1, string.Empty,
                $"Failed to start '{fileName}': {ex.Message}");
        }
        if (process == null)
        {
            return new PreBuildStepResult(stepIndex, commandPreview, -1, string.Empty,
                "Process.Start returned null.");
        }

        using (process)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(PerStepTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* swallow */ }
                return new PreBuildStepResult(stepIndex, commandPreview, -1,
                    stdout.ToString(),
                    stderr.ToString() + $"\n(timed out after {PerStepTimeoutMs / 1000}s)");
            }
            process.WaitForExit();

            return new PreBuildStepResult(stepIndex, commandPreview, process.ExitCode,
                stdout.ToString(), stderr.ToString());
        }
    }

    // ---- Helpers -----------------------------------------------------------

    private static string ResolvePath(string relOrAbs, string cwd)
    {
        if (string.IsNullOrEmpty(relOrAbs)) return cwd;
        return Path.IsPathRooted(relOrAbs) ? relOrAbs : Path.Combine(cwd, relOrAbs);
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(src, dst));
        }
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(src, dst), overwrite: true);
        }
    }

    private static IReadOnlyList<string> Combine(string head, IReadOnlyList<string> rest)
    {
        var list = new List<string>(rest.Count + 1) { head };
        list.AddRange(rest);
        return list;
    }

    /// <summary>
    /// Tiny shell-style splitter: respects "double" and 'single' quotes
    /// but does not implement escapes — the user is editing a Unity
    /// editor field, not a shell.
    /// </summary>
    private static IReadOnlyList<string> ParseCommandLine(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var current = new StringBuilder();
        char quote = '\0';
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }
        return result;
    }

    private static string JoinForDisplay(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            var a = args[i];
            if (a.Contains(' ') || a.Contains('\t'))
            {
                sb.Append('"').Append(a).Append('"');
            }
            else
            {
                sb.Append(a);
            }
        }
        return sb.ToString();
    }
}
