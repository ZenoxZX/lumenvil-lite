using System.Diagnostics;
using System.Text;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

public sealed class GitRunner
{
    private const int PerStepTimeoutMs = 5 * 60 * 1000; // 5 minutes per step.

    /// <summary>
    /// Runs the steps in order. Returns a list of results. If any step
    /// produces a non-zero exit code the chain stops at that step;
    /// successful results before it are still in the list.
    /// </summary>
    public IReadOnlyList<PreBuildStepResult> Run(
        IReadOnlyList<GitStep> steps,
        string workingDirectory)
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
            var args = BuildArgs(step);
            var commandPreview = "git " + JoinForDisplay(args);

            var result = RunGit(args, workingDirectory, i, commandPreview);
            results.Add(result);
            if (result.ExitCode != 0)
            {
                break; // Stop on the first failure.
            }
        }
        return results;
    }

    private static PreBuildStepResult RunGit(
        IReadOnlyList<string> args,
        string workingDirectory,
        int stepIndex,
        string commandPreview)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
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

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new PreBuildStepResult(
                StepIndex: stepIndex,
                Command: commandPreview,
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: $"Failed to start git: {ex.Message}");
        }
        if (process == null)
        {
            return new PreBuildStepResult(
                StepIndex: stepIndex,
                Command: commandPreview,
                ExitCode: -1,
                Stdout: string.Empty,
                Stderr: "Process.Start returned null.");
        }

        using (process)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) stderr.AppendLine(e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(PerStepTimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* swallow */ }
                return new PreBuildStepResult(
                    StepIndex: stepIndex,
                    Command: commandPreview,
                    ExitCode: -1,
                    Stdout: stdout.ToString(),
                    Stderr: stderr.ToString() + $"\n(timed out after {PerStepTimeoutMs / 1000}s)");
            }
            // Drain any pending output the async readers might still hold.
            process.WaitForExit();

            return new PreBuildStepResult(
                StepIndex: stepIndex,
                Command: commandPreview,
                ExitCode: process.ExitCode,
                Stdout: stdout.ToString(),
                Stderr: stderr.ToString());
        }
    }

    private static IReadOnlyList<string> BuildArgs(GitStep step)
    {
        var kind = (step.Kind ?? "preset").Trim().ToLowerInvariant();
        if (kind == "custom")
        {
            var raw = step.CustomCommand ?? string.Empty;
            return ParseCommandLine(raw);
        }

        var preset = (step.Preset ?? string.Empty).Trim().ToLowerInvariant();
        var extra = ParseCommandLine(step.Args ?? string.Empty);

        switch (preset)
        {
            case "fetch":    return Combine("fetch", extra);
            case "pull":     return Combine("pull", extra);
            case "checkout": return Combine("checkout", extra);
            case "restore":  return Combine("restore", extra.Count == 0 ? new[] { "." } : extra);
            case "reset":    return Combine("reset", extra.Count == 0 ? new[] { "--hard" } : extra);
            case "status":   return Combine("status", extra);
            case "clean":    return Combine("clean", extra.Count == 0 ? new[] { "-fd" } : extra);
            default:         return Combine("status", Array.Empty<string>());
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
    /// editor field, not a shell. Good enough for "origin dev",
    /// "origin/dev -- some path", '"branch with spaces"', etc.
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
