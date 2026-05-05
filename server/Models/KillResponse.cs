namespace LumenvilLite.Models;

public sealed record KillRequest(bool Force = false);

public sealed record KillResponse(
    int Pid,
    bool Killed,
    string Method,
    int? ExitCode,
    string? Error);
