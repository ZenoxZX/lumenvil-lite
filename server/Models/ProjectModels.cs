namespace LumenvilLite.Models;

public sealed record ProjectEntry(
    string Name,
    string ProjectPath,
    string ExecuteMethod);

public sealed record ProjectListResponse(IReadOnlyList<ProjectEntry> Projects);
