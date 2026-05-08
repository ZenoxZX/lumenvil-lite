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

    // Unity build flags. Development is the master switch; the other three
    // are no-ops in a release build, so the builder ignores them when
    // Development=false (defence-in-depth in case the UI ever sends them
    // anyway). Defaults match Unity's own "release build" out-of-the-box.
    public bool Development { get; init; } = false;
    public bool AutoConnectProfiler { get; init; } = false;
    public bool DeepProfiling { get; init; } = false;
    public bool ScriptDebugging { get; init; } = false;
}

public sealed record ActiveBuildInfo(
    string ProjectName,
    string Target,
    BuildBackend Backend,
    string OutputPath,
    string LogFilePath,
    int Pid,
    DateTime StartedAtUtc)
{
    /// <summary>
    /// Pre-build step results captured before this build's Unity process
    /// was spawned. Empty when the build was started without
    /// <c>runPreBuildSteps</c>.
    /// </summary>
    public IReadOnlyList<PreBuildStepResult> PreBuildResults { get; init; } = Array.Empty<PreBuildStepResult>();
}

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
    DateTime FinishedAtUtc)
{
    /// <summary>
    /// Pre-build step results captured before the Unity process was
    /// spawned (or, on prebuild_failed, the partial chain up to and
    /// including the failing step).
    /// </summary>
    public IReadOnlyList<PreBuildStepResult> PreBuildResults { get; init; } = Array.Empty<PreBuildStepResult>();
}

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
