namespace LumenvilLite.Models;

/// <summary>
/// One step in a pre-build or post-build chain. Two shapes:
///
///   Kind=preset → identified by (Group, Subset, Args)
///       Group   = "git" | "filesystem" | "notify"
///       Subset  = depends on Group (e.g. "fetch" for git, "copy" for filesystem)
///       Args    = whitespace-split argument list (quotes honoured)
///
///   Kind=custom → free-form command run through an interpreter
///       Interpreter = "bash" | "cmd" | "pwsh" | "direct"
///       Command     = the literal command line passed to the interpreter
///
/// Old-style steps (single-level kind/preset/customCommand) are not
/// migrated — projects.json is wiped on schema change.
/// </summary>
public sealed record StepDefinition
{
    public string Kind { get; init; } = "preset";

    // Preset branch.
    public string? Group { get; init; }
    public string? Subset { get; init; }
    public string? Args { get; init; }

    // Custom branch.
    public string? Interpreter { get; init; }
    public string? Command { get; init; }
}

public sealed record PreBuildStepResult(
    int StepIndex,
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr);
