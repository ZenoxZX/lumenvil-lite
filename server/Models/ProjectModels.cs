namespace LumenvilLite.Models;

public sealed record ProjectEntry(
    string Name,
    string ProjectPath,
    string ExecuteMethod)
{
    /// <summary>
    /// Optional list of git steps the server runs (in order, against
    /// <see cref="ProjectPath"/>) before spawning the Unity build.
    /// Empty / missing means no pre-build steps for this project.
    /// </summary>
    public IReadOnlyList<GitStep> PreBuildSteps { get; init; } = Array.Empty<GitStep>();
}

public sealed record ProjectListResponse(IReadOnlyList<ProjectEntry> Projects);
