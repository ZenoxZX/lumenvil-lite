# Changelog

All notable changes to the Lumenvil Lite Unity package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this package adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] — Unreleased

### Added
- Editor window under **Tools → Lumenvil Lite** that polls `/status` on a configurable interval.
- Connection indicator (green / red / gray dot) with ping time.
- Unity process panel that distinguishes Editor sessions from batch builds.
- Build status panel with current phase, last log line, and a foldout log tail.
- `EditorApplication.Beep` + in-window notification when a build leaves the "Building" state.
- Settings persisted via `EditorPrefs` (host, port, poll interval, timeout).
- Per-process **Quit** and **Force** buttons that ask the server to kill the remote Unity process. Quit goes through `CloseMainWindow` first (and falls back to a hard kill after a 5s timeout), Force calls `Process.Kill` immediately. Both prompt for confirmation in the editor before sending.
