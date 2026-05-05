using System.Text.RegularExpressions;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

public sealed class UnityLogWatcher
{
    private const int TailLineCount = 50;
    private const int IdleAfterMinutes = 30;

    private static readonly Regex BuildStartRegex = new(
        @"(Building Player|BuildPlayer|Build started)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BuildSuccessRegex = new(
        @"(Build succeeded|Build Report|Build completed with a result of 'Succeeded')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BuildFailedRegex = new(
        @"(Build Failed|Build completed with a result of 'Failed'|error CS\d+|FATAL ERROR)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BuildCancelledRegex = new(
        @"(\*\*\* Cancelled|Build cancelled)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CompilingRegex = new(
        @"(Compiling scripts|Compiling shader|Reloading assemblies)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public BuildStatusResponse Inspect(string? candidateLogPath)
    {
        var path = ResolveLogPath(candidateLogPath);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return new BuildStatusResponse(
                Status: BuildStatus.Idle,
                CurrentPhase: null,
                LastLogLine: null,
                LogTail: Array.Empty<string>(),
                StartedAtUtc: null,
                FinishedAtUtc: null,
                ErrorSummary: null,
                LogFilePath: path);
        }

        var fileInfo = new FileInfo(path);
        var lastWriteUtc = fileInfo.LastWriteTimeUtc;
        var staleness = DateTime.UtcNow - lastWriteUtc;

        var lines = ReadTail(path, TailLineCount);
        var lastLine = lines.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));

        var status = ClassifyStatus(lines, staleness);
        var phase = DetectPhase(lines);
        var error = status == BuildStatus.Failed ? FindErrorSummary(lines) : null;

        DateTime? finishedAt = status is BuildStatus.Success or BuildStatus.Failed or BuildStatus.Cancelled
            ? lastWriteUtc
            : null;

        return new BuildStatusResponse(
            Status: status,
            CurrentPhase: phase,
            LastLogLine: lastLine,
            LogTail: lines,
            StartedAtUtc: null,
            FinishedAtUtc: finishedAt,
            ErrorSummary: error,
            LogFilePath: path);
    }

    private static string? ResolveLogPath(string? candidate)
    {
        if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            return null;
        }

        var defaultPath = Path.Combine(localAppData, "Unity", "Editor", "Editor.log");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    private static BuildStatus ClassifyStatus(IReadOnlyList<string> lines, TimeSpan staleness)
    {
        bool sawStart = false;
        bool sawSuccess = false;
        bool sawFailed = false;
        bool sawCancelled = false;
        int lastStartIdx = -1;
        int lastTerminalIdx = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (BuildStartRegex.IsMatch(line))
            {
                sawStart = true;
                lastStartIdx = i;
            }
            if (BuildSuccessRegex.IsMatch(line))
            {
                sawSuccess = true;
                lastTerminalIdx = i;
            }
            if (BuildFailedRegex.IsMatch(line))
            {
                sawFailed = true;
                lastTerminalIdx = i;
            }
            if (BuildCancelledRegex.IsMatch(line))
            {
                sawCancelled = true;
                lastTerminalIdx = i;
            }
        }

        if (sawStart && lastStartIdx > lastTerminalIdx && staleness < TimeSpan.FromMinutes(IdleAfterMinutes))
        {
            return BuildStatus.Building;
        }

        if (sawCancelled && lastTerminalIdx >= lastStartIdx)
        {
            return BuildStatus.Cancelled;
        }
        if (sawFailed && lastTerminalIdx >= lastStartIdx)
        {
            return BuildStatus.Failed;
        }
        if (sawSuccess && lastTerminalIdx >= lastStartIdx)
        {
            return BuildStatus.Success;
        }

        return BuildStatus.Idle;
    }

    private static string? DetectPhase(IReadOnlyList<string> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (BuildStartRegex.IsMatch(line))
            {
                return "Building Player";
            }
            if (CompilingRegex.IsMatch(line))
            {
                return "Compiling";
            }
        }
        return null;
    }

    private static string? FindErrorSummary(IReadOnlyList<string> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                && line.Length < 500)
            {
                return line.Trim();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ReadTail(string path, int maxLines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var queue = new Queue<string>(maxLines);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (queue.Count == maxLines)
                {
                    queue.Dequeue();
                }
                queue.Enqueue(line);
            }
            return queue.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
