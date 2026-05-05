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
├── server/                     # Windows side (.NET 8 minimal API)
│   ├── LumenvilLite.csproj
│   ├── Program.cs
│   ├── Endpoints/
│   ├── Services/
│   └── Models/
└── unity-editor-window/        # Unity side (Editor scripts + asmdef)
    ├── LumenvilLite.Editor.asmdef
    ├── LumenvilLiteWindow.cs
    ├── Services/
    ├── Settings/
    └── Models/
```

## Setup

### Windows (build machine)

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Copy `server/` somewhere on the Windows host (e.g. `C:\Tools\LumenvilLite\`).
3. Run it:
   ```powershell
   cd C:\Tools\LumenvilLite
   dotnet run
   ```
   Expected output:
   ```
   Lumenvil Lite listening on http://0.0.0.0:5151
   ```
4. Open the firewall port (one-time):
   ```powershell
   New-NetFirewallRule -DisplayName "Lumenvil Lite" -Direction Inbound -Protocol TCP -LocalPort 5151 -Action Allow
   ```

### Unity (any OS)

1. Copy the contents of `unity-editor-window/` into your project under any Editor folder, e.g. `Assets/Editor/LumenvilLite/`.
2. The asmdef references [UniTask](https://github.com/Cysharp/UniTask) — install it via UPM if your project does not already have it.
3. In Unity's Player Settings, set **Allow downloads over HTTP** to **Always allowed** (the server speaks plain HTTP on the LAN).
4. Open **Tools → Lumenvil Lite**.
5. Click **Settings**, set:
   - **Host**: the Windows host's mDNS name (e.g. `my-build-pc.local`) or its LAN IP
   - **Port**: `5151` (default)
6. The connection dot should turn green within a few seconds.

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
- Build status detection is regex-based over `Editor.log`. Edge cases or non-English Unity locales may need tuning in `Services/UnityLogWatcher.cs`.
- Process classification (Editor vs. batch build) requires WMI access to read the command line of `Unity.exe`. If WMI is blocked, the type column falls back to "Unknown".

## License

MIT.
