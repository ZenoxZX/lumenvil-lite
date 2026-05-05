using System.Text.Json.Serialization;
using LumenvilLite.Endpoints;
using LumenvilLite.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5151);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<UnityProcessScanner>();
builder.Services.AddSingleton<UnityLogWatcher>();
builder.Services.AddSingleton<ProjectStore>();
builder.Services.AddSingleton<BuildLauncher>();
builder.Services.AddSingleton(_ => new ServerInfo(DateTime.UtcNow));

var app = builder.Build();

app.MapHealthEndpoint();
app.MapUnityEndpoint();
app.MapBuildEndpoint();
app.MapStatusEndpoint();
app.MapKillEndpoint();
app.MapProjectsEndpoint();
app.MapBuildControlEndpoint();

app.Logger.LogInformation("Lumenvil Lite listening on http://0.0.0.0:5151");
app.Run();

public sealed record ServerInfo(DateTime StartedAtUtc);
