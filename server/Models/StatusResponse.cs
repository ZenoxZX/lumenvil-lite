namespace LumenvilLite.Models;

public sealed record StatusResponse(
    HealthResponse Health,
    UnityResponse Unity,
    BuildStatusResponse Build,
    ActiveBuildInfo? ActiveBuild);
