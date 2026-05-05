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
            ├── Services/
            ├── Settings/
            └── Models/
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

| Method | Path      | Purpose                                                              |
|--------|-----------|----------------------------------------------------------------------|
| GET    | `/health` | Server identity, uptime, hostname                                    |
| GET    | `/unity`  | Running Unity processes (Editor vs. BatchBuild, RAM, uptime, path)   |
| GET    | `/build`  | Current build status from the Unity log + tail of recent log lines   |
| GET    | `/status` | Aggregated `health` + `unity` + `build` (used by the editor window)  |

## Limitations

- LAN only — there is no authentication. Do not expose port 5151 to the internet.
- Build status detection is regex-based over `Editor.log`. Edge cases or non-English Unity locales may need tuning in `server/Services/UnityLogWatcher.cs`.
- Process classification (Editor vs. batch build) requires WMI access to read the command line of `Unity.exe`. If WMI is blocked, the type column falls back to "Unknown".

## License

MIT.
