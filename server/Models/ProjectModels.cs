namespace LumenvilLite.Models;

public sealed record ProjectEntry(
    string Name,
    string ProjectPath,
    string ExecuteMethod)
{
    /// <summary>
    /// Steps the server runs (in order, against <see cref="ProjectPath"/>)
    /// before spawning the Unity build. Empty / missing means no pre-build
    /// steps for this project.
    /// </summary>
    public IReadOnlyList<StepDefinition> PreBuildSteps { get; init; } = Array.Empty<StepDefinition>();

    /// <summary>
    /// Steps the server runs after the Unity build process exits — regardless
    /// of outcome, so notify steps can report failures too. Each step sees
    /// LUMENVIL_OUTCOME / LUMENVIL_EXIT_CODE / LUMENVIL_PROJECT / LUMENVIL_TARGET
    /// / LUMENVIL_OUTPUT in its environment.
    /// </summary>
    public IReadOnlyList<StepDefinition> PostBuildSteps { get; init; } = Array.Empty<StepDefinition>();
}

public sealed record ProjectListResponse(IReadOnlyList<ProjectEntry> Projects);
