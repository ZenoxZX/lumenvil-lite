# Lumenvil Lite

A small HTTP service that runs on a Windows build machine, plus a Unity Editor window that polls it from a Mac (or any other machine) over the local network.

## What it shows

- **Reachability** — is the build machine up? (green / red dot)
- **Unity processes** — interactive Editor vs. batch build, RAM usage, project path, uptime
- **Build status** — read directly from the Unity log: idle / building / success / failed / cancelled
- **Build-finished notification** — the editor window detects the transition out of "Building" and shows a Unity notification with a beep

## Architecture

```
Mac / dev machine                       Windows build machine
─────────────────                       ─────────────────────
Unity Editor Window  ── HTTP GET ──▶    Lumenvil Lite (.NET 8)
                     ◀── JSON ──        listening on :5151
                                          ├── scans Unity.exe processes
                                          └── tails Unity Editor.log
```

- Server: .NET 8 minimal API, single process, port `5151`, no auth (LAN only).
- Client: Unity Editor window, polls `/status` on a configurable interval via `UnityWebRequest`.

## Repository layout

```
lumenvil-lite/
├── README.md
├── LICENSE
├── server/                       # Windows side (.NET 8 minimal API)
│   ├── LumenvilLite.csproj
│   ├── Program.cs
│   ├── Endpoints/
│   ├── Services/
│   ├── Models/
│   └── scripts/
│       └── install-task.ps1      # Publish + register-on-logon installer
└── Unity/
    └── LumenvilLite/             # Unity side (UPM package)
        ├── package.json
        ├── README.md
        ├── CHANGELOG.md
        ├── LICENSE.md
        └── Editor/
            ├── LumenvilLite.Editor.asmdef
            ├── LumenvilLiteWindow.cs
            ├── Build/
            │   └── LumenvilLiteBuilder.cs    # Default builder + template
            ├── Models/
            ├── Services/
            ├── Settings/
            └── UI/
                ├── ProjectManagerWindow.cs
                └── ProjectStepsWindow.cs     # Pre-build git steps editor
```

## Setup

### Windows (build machine)

The recommended path publishes a single self-contained `.exe` and registers
it as a scheduled task that starts on user logon. Once installed, the
machine just has to be powered on and logged in — no terminal stays open,
no `dotnet run` needed.

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on the Windows host.
2. Clone or copy this repository to the host.
3. Open **PowerShell as Administrator**, then from the repo root:
   ```powershell
   cd server\scripts
   .\install-task.ps1
   ```
   The script:
   - publishes a single-file self-contained binary into `C:\Tools\LumenvilLite\`,
   - opens TCP 5151 in the firewall,
   - registers a scheduled task named **Lumenvil Lite** that runs on logon,
   - starts the task immediately so you don't have to log out and back in.

   Verify with:
   ```powershell
   curl http://localhost:5151/health
   ```

4. To remove everything later:
   ```powershell
   .\install-task.ps1 -Uninstall
   ```

#### Manual / dev mode

If you'd rather run it ad-hoc without installing a task:

```powershell
cd server
dotnet run
```

This is mostly useful while iterating on the server itself.

### Unity (any OS)

The Unity side ships as a UPM package under `Unity/LumenvilLite/`. UniTask
must be installed first because Unity's Package Manager does not resolve
Git-URL dependencies declared inside a package.

1. In Unity, open **Window → Package Manager**.
2. Click **+** → **Add package from git URL...** and add UniTask:
   ```
   https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
   ```
3. Click **+** → **Add package from git URL...** again and add Lumenvil Lite:
   ```
   https://github.com/ZenoxZX/lumenvil-lite.git?path=Unity/LumenvilLite
   ```
4. In **Player Settings**, set **Allow downloads over HTTP** to **Always allowed** (the server speaks plain HTTP on the LAN).
5. Open **Tools → Lumenvil Lite**.
6. Click **Settings**, set:
   - **Host**: the Windows host's mDNS name (e.g. `my-build-pc.local`) or its LAN IP
   - **Port**: `5151` (default)
7. The connection dot should turn green within a few seconds.

See [Unity/LumenvilLite/README.md](Unity/LumenvilLite/README.md) for package details.

## Quick test from a terminal

```bash
curl http://<windows-host>:5151/health
curl http://<windows-host>:5151/status
```

## Endpoints

| Method | Path                  | Purpose                                                              |
|--------|-----------------------|----------------------------------------------------------------------|
| GET    | `/health`             | Server identity, uptime, hostname                                    |
| GET    | `/unity`              | Running Unity processes (Editor vs. BatchBuild, RAM, uptime, path)   |
| GET    | `/build`              | Current build status from the Unity log + tail of recent log lines   |
| GET    | `/status`             | Aggregated `health` + `unity` + `build` (used by the editor window)  |
| POST   | `/unity/{pid}/kill`   | Quit (graceful) or force-kill a Unity process                        |
| GET    | `/projects`           | List build projects                                                  |
| POST   | `/projects`           | Register a build project (`name`, `projectPath`, `executeMethod`, `preBuildSteps`) |
| PUT    | `/projects/{name}`    | Replace a project entry (rename allowed if the new name is free)      |
| DELETE | `/projects/{name}`    | Remove a project                                                     |
| POST   | `/build/start`        | Launch a batch-mode Unity build                                      |
| GET    | `/build/active`       | Active build info or null                                            |
| POST   | `/build/cancel`       | Kill the active build                                                |

## Pre-build and post-build steps

Each registered project carries two ordered lists of steps — one for **pre-build** (run before Unity spawns; first non-zero exit cancels the build) and one for **post-build** (run after Unity exits, regardless of outcome, so failures can still notify). Each step is one of:

- **Preset → Git**: Fetch, Pull, Checkout, Restore, Reset, Status, Clean, Tag.
- **Preset → Filesystem**: Copy, Move, Delete, Mkdir, Zip (paths resolved against the project path).
- **Preset → Notify**: Slack webhook, Discord webhook, generic HTTP POST.
- **Custom**: free-form command line run through your choice of interpreter — `bash` (Git for Windows `bash.exe`, default), `cmd`, `pwsh`, or `direct` (no shell, first token is the executable).

Post-build steps see the build outcome in their environment: `LUMENVIL_OUTCOME`, `LUMENVIL_EXIT_CODE`, `LUMENVIL_PROJECT`, `LUMENVIL_TARGET`, `LUMENVIL_OUTPUT`. Slack/Discord notify steps prebake those into their webhook body automatically.

Manage both lists from the editor window via **Edit Steps...** — the popup has Pre-build / Post-build tabs. Steps live in `projects.json` on the server, so every client that talks to the same server sees the same lists. The build response and `last-build.json` both carry `preBuildResults` and `postBuildResults` arrays (stdout / stderr / exit code per step). The Build status panel renders these as foldouts with **Copy** buttons.

## Build script contract

`POST /build/start` invokes Unity in batch mode and runs your registered `executeMethod`.
**You can also leave `executeMethod` empty** when registering a project — the server then falls back to `LumenvilLiteBuilder.Build`, the default builder shipped with the Unity package (lives in namespace `LumenvilLite.Editor.Build`, but Unity's `-executeMethod` parser only accepts the leaf `ClassName.MethodName`). It reads the same arguments documented below, builds the scenes that are enabled in **Build Settings**, and exits non-zero on failure. The same file (`Unity/LumenvilLite/Editor/Build/LumenvilLiteBuilder.cs`) is also the recommended starting point if you want to write your own — copy it into your project, rename the method, and tweak.
Lumenvil Lite passes these custom CLI arguments alongside Unity's own:

| Argument             | Value                                                              |
|----------------------|--------------------------------------------------------------------|
| `-lumenvilTarget`    | The build target (currently `StandaloneWindows64`)                 |
| `-lumenvilBackend`   | `Il2cpp` or `Mono`                                                 |
| `-lumenvilOutput`    | Output directory, already created under `C:\Builds\<project>\...`  |
| `-lumenvilDefines`   | Semicolon-separated scripting defines (only when non-empty)        |

Your build script reads them with `Environment.GetCommandLineArgs()` and writes the player into the supplied output path. Example:

```csharp
public static class BuildScript
{
    public static void BuildFromLumenvil()
    {
        var args = System.Environment.GetCommandLineArgs();
        string GetArg(string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        var target  = GetArg("-lumenvilTarget");
        var backend = GetArg("-lumenvilBackend");
        var output  = GetArg("-lumenvilOutput");
        var defines = GetArg("-lumenvilDefines");

        var buildTarget = (BuildTarget)System.Enum.Parse(typeof(BuildTarget), target);
        var group = BuildPipeline.GetBuildTargetGroup(buildTarget);

        PlayerSettings.SetScriptingBackend(group,
            backend == "Il2cpp"
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x);

        if (!string.IsNullOrEmpty(defines))
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled).Select(s => s.path).ToArray();

        BuildPipeline.BuildPlayer(scenes,
            System.IO.Path.Combine(output, "Game.exe"),
            buildTarget, BuildOptions.None);
    }
}
```

Register the project with `executeMethod = "BuildScript.BuildFromLumenvil"` from the Lumenvil Lite editor window's **Manage Projects...** popup.

## Limitations

- LAN only — there is no authentication. Do not expose port 5151 to the internet.
- Build status detection is regex-based over `Editor.log`. Edge cases or non-English Unity locales may need tuning in `server/Services/UnityLogWatcher.cs`.
- Process classification (Editor vs. batch build) requires WMI access to read the command line of `Unity.exe`. If WMI is blocked, the type column falls back to "Unknown".

## License

MIT.
