namespace LumenvilLite.Models;

public enum BuildBackend
{
    Il2cpp,
    Mono
}

public sealed record BuildStartRequest(
    string ProjectName,
    string Target,
    BuildBackend Backend,
    string? Defines);

public sealed record ActiveBuildInfo(
    string ProjectName,
    string Target,
    BuildBackend Backend,
    string OutputPath,
    string LogFilePath,
    int Pid,
    DateTime StartedAtUtc);

public sealed record BuildStartResponse(
    bool Started,
    ActiveBuildInfo? Build,
    string? Error,
    string? ErrorCode);

public sealed record ActiveBuildResponse(ActiveBuildInfo? Active);

public sealed record BuildCancelResponse(
    bool Cancelled,
    string? Error);
