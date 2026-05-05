# Changelog

All notable changes to the Lumenvil Lite Unity package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/).

## [0.2.0] — Unreleased

### Added
- **Build Trigger** card on the main editor window — pick a registered project, target (currently `StandaloneWindows64`), scripting backend (`Il2cpp` / `Mono`), optional defines, and start a remote build.
- **Manage Projects...** popup (`ProjectManagerWindow`) for adding, listing, and removing build projects on the server. `executeMethod` is now optional — leave it empty to fall back to the package's built-in builder.
- **Built-in builder** at `LumenvilLite.Editor.Build.LumenvilLiteBuilder.Build`: reads the `-lumenvil*` args, applies the requested backend / defines, builds the scenes enabled in `EditorBuildSettings`, and exits non-zero on failure. Doubles as a starter template — copy it into your own project to add Addressables, version stamping, etc.
- Build outcome is now driven by the spawned Unity process's exit code (with a polling fallback) and surfaced through `lastBuild` on `/status`. The window shows a coloured banner (success / failed / cancelled), the exit code, and timestamps.
- Cancel button that kills the active build process via the server.
- Pre-flight check that warns the user if a Unity Editor is already open on the same project path before sending a build request.

### Server contract
The server invokes the user's `executeMethod` and passes:
- `-lumenvilTarget <target>`
- `-lumenvilBackend Il2cpp|Mono`
- `-lumenvilOutput <path>` (Lumenvil Lite has already created this directory under `C:\Builds\<project>\<target>\<timestamp>\`)
- `-lumenvilDefines <FOO;BAR>` (only when defines are non-empty)

The build script reads them via `Environment.GetCommandLineArgs()` and is responsible for writing the player into the output directory.

## [0.1.0] — Unreleased

### Added
- Editor window under **Tools → Lumenvil Lite** that polls `/status` on a configurable interval.
- Connection indicator (green / red / gray dot) with ping time.
- Unity process panel that distinguishes Editor sessions from batch builds.
- Build status panel with current phase, last log line, and a foldout log tail.
- `EditorApplication.Beep` + in-window notification when a build leaves the "Building" state.
- Settings persisted via `EditorPrefs` (host, port, poll interval, timeout).
- Per-process **Quit** and **Force** buttons that ask the server to kill the remote Unity process. Quit goes through `CloseMainWindow` first (and falls back to a hard kill after a 5s timeout), Force calls `Process.Kill` immediately. Both prompt for confirmation in the editor before sending.
