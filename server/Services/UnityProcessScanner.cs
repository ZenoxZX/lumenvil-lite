using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using LumenvilLite.Models;

namespace LumenvilLite.Services;

[SupportedOSPlatform("windows")]
public sealed class UnityProcessScanner
{
    private static readonly string[] UnityProcessNames = { "Unity" };
    private static readonly Regex ProjectPathRegex = new(
        "-projectPath\\s+\"([^\"]+)\"|-projectPath\\s+(\\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LogFileRegex = new(
        "-logFile\\s+\"([^\"]+)\"|-logFile\\s+(\\S+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public UnityResponse Scan()
    {
        var commandLines = TryReadCommandLines();
        var results = new List<UnityProcessInfo>();

        foreach (var name in UnityProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    commandLines.TryGetValue(process.Id, out var commandLine);
                    if (!IsMainEditorProcess(commandLine))
                    {
                        continue;
                    }

                    var type = ClassifyProcess(commandLine);
                    var projectPath = ExtractMatch(commandLine, ProjectPathRegex);
                    var logFile = ExtractMatch(commandLine, LogFileRegex);
                    var uptime = (DateTime.Now - process.StartTime).TotalSeconds;

                    results.Add(new UnityProcessInfo(
                        Pid: process.Id,
                        Type: type,
                        ProjectPath: projectPath,
                        RamBytes: process.WorkingSet64,
                        UptimeSeconds: Math.Round(uptime, 1),
                        LogFilePath: logFile));
                }
                catch
                {
                    // Process may have exited or access denied — skip silently.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return new UnityResponse(
            Running: results.Count > 0,
            Count: results.Count,
            Processes: results);
    }

    private static bool IsMainEditorProcess(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            // No command line means we cannot tell — be conservative and drop it,
            // sub-processes (shader compiler, package manager, crash handler) are
            // far more common than the rare access-denied case on the main editor.
            return false;
        }

        return ProjectPathRegex.IsMatch(commandLine);
    }

    private static UnityProcessType ClassifyProcess(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return UnityProcessType.Unknown;
        }

        if (commandLine.Contains("-batchmode", StringComparison.OrdinalIgnoreCase))
        {
            return UnityProcessType.BatchBuild;
        }

        return UnityProcessType.Editor;
    }

    private static string? ExtractMatch(string? input, Regex regex)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        var match = regex.Match(input);
        if (!match.Success)
        {
            return null;
        }

        var quoted = match.Groups[1].Value;
        return string.IsNullOrEmpty(quoted) ? match.Groups[2].Value : quoted;
    }

    private static Dictionary<int, string> TryReadCommandLines()
    {
        var map = new Dictionary<int, string>();
        if (!OperatingSystem.IsWindows())
        {
            return map;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'Unity.exe'");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                    map[pid] = commandLine;
                }
            }
        }
        catch
        {
            // WMI may fail (permissions, service stopped). Fallback: classify Unknown.
        }

        return map;
    }
}
