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

This package depends on [UniTask](https://github.com/Cysharp/UniTask). Unity's
Package Manager does not resolve Git URLs declared inside a package's
`dependencies`, so UniTask has to be added to the project first.

1. Open **Window → Package Manager**.
2. Click **+** → **Add package from git URL...** and add UniTask:
   ```
   https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
   ```
3. Click **+** → **Add package from git URL...** again and add Lumenvil Lite:
   ```
   https://github.com/ZenoxZX/lumenvil-lite.git?path=Unity/LumenvilLite
   ```

If UniTask is missing the package will fail to compile — install it first.

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
