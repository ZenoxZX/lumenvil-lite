namespace LumenvilLite.Models;

public enum BuildStatus
{
    Idle,
    Building,
    Success,
    Failed,
    Cancelled,
    Unknown
}

public sealed record BuildStatusResponse(
    BuildStatus Status,
    string? CurrentPhase,
    string? LastLogLine,
    IReadOnlyList<string> LogTail,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? ErrorSummary,
    string? LogFilePath);
