namespace LumenvilLite.Models;

/// <summary>
/// One git invocation that runs against a project before its build is
/// allowed to start. <c>Kind</c> is either "preset" (use <see cref="Preset"/>
/// + optional <see cref="Args"/>) or "custom" (parse <see cref="CustomCommand"/>
/// into git args directly).
/// </summary>
public sealed record GitStep
{
    public string Kind { get; init; } = "preset";
    public string? Preset { get; init; }
    public string? Args { get; init; }
    public string? CustomCommand { get; init; }
}

public sealed record PreBuildStepResult(
    int StepIndex,
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr);
