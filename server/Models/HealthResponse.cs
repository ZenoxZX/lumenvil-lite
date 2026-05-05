namespace LumenvilLite.Models;

public sealed record HealthResponse(
    string Status,
    string Name,
    string Version,
    string Hostname,
    double UptimeSeconds);
