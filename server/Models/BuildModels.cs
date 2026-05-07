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
    string? Defines)
{
    /// <summary>
    /// When true, the project's PreBuildSteps run before Unity is spawned.
    /// When false (or omitted), the pre-build steps are skipped — useful
    /// for "I just want to test the build, don't touch git" runs.
    /// </summary>
    public bool RunPreBuildSteps { get; init; } = false;
}

public sealed record ActiveBuildInfo(
    string ProjectName,
    string Target,
    BuildBackend Backend,
    string OutputPath,
    string LogFilePath,
    int Pid,
    DateTime StartedAtUtc);

public enum LastBuildOutcome
{
    Unknown,
    Success,
    Failed,
    Cancelled
}

public sealed record LastBuildInfo(
    string ProjectName,
    string Target,
    BuildBackend Backend,
    string OutputPath,
    string LogFilePath,
    LastBuildOutcome Outcome,
    int ExitCode,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc);

public sealed record BuildStartResponse(
    bool Started,
    ActiveBuildInfo? Build,
    string? Error,
    string? ErrorCode)
{
    /// <summary>
    /// Results of the pre-build git steps if any ran, in order.
    /// On <c>prebuild_failed</c> the last entry holds the failing
    /// step's stderr; on success this is the full successful chain
    /// for traceability.
    /// </summary>
    public IReadOnlyList<PreBuildStepResult> PreBuildResults { get; init; } = Array.Empty<PreBuildStepResult>();
}

public sealed record ActiveBuildResponse(ActiveBuildInfo? Active);

public sealed record BuildCancelResponse(
    bool Cancelled,
    string? Error);
