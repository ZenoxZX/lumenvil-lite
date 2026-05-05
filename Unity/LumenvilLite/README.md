# Lumenvil Lite — Unity Package

Editor window that polls a remote **Lumenvil Lite** HTTP server (running on a Windows build machine) and surfaces the build machine's state inside the Unity Editor.

This is the Unity-side half of the project. The companion .NET 8 server lives at the [repository root](https://github.com/ZenoxZX/lumenvil-lite) under `server/`.

## Features

- **Reachability indicator** — green / red dot with ping time
- **Unity process panel** — Editor vs. batch build, RAM, uptime, project path
- **Build status panel** — idle / building / success / failed / cancelled, current phase, last log line, foldout log tail
- **Build-finished notification** — Unity in-editor notification + beep when status leaves "Building"
- **Settings** — host, port, poll interval, timeout (persisted in `EditorPrefs`)

## Installation (Package Manager — Git URL)

1. Open **Window → Package Manager**.
2. Click **+** → **Add package from git URL...**
3. Enter:
   ```
   https://github.com/ZenoxZX/lumenvil-lite.git?path=Unity/LumenvilLite
   ```

The package declares [UniTask](https://github.com/Cysharp/UniTask) as a Git dependency; Unity will fetch it automatically.

## Usage

1. Make sure the Lumenvil Lite server is running on the target Windows host (see the [main README](https://github.com/ZenoxZX/lumenvil-lite#readme)).
2. In Unity's **Player Settings**, set **Allow downloads over HTTP** to **Always allowed** — the server speaks plain HTTP on the LAN.
3. Open **Tools → Lumenvil Lite**.
4. Click **Settings**, set the Windows host's mDNS name (e.g. `my-build-pc.local`) or its LAN IP, and the port (default `5151`).
5. The connection dot turns green within a few seconds.

## Requirements

- Unity 2021.3 LTS or newer
- UniTask (declared as a package dependency, installed automatically)
- A reachable Lumenvil Lite server on the LAN

## License

MIT — see [LICENSE.md](LICENSE.md).
