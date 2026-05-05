namespace LumenvilLite.Models;

public enum UnityProcessType
{
    Unknown,
    Editor,
    BatchBuild
}

public sealed record UnityProcessInfo(
    int Pid,
    UnityProcessType Type,
    string? ProjectPath,
    long RamBytes,
    double UptimeSeconds,
    string? LogFilePath);

public sealed record UnityResponse(
    bool Running,
    int Count,
    IReadOnlyList<UnityProcessInfo> Processes);
