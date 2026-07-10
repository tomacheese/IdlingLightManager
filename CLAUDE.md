# CLAUDE.md

## Project overview

IdlingLightManager is a Windows tray-resident application that watches keyboard/mouse
idle time and turns an IoT light ON/OFF through an HTTP API. It turns the light OFF once
the machine has been idle past a threshold, turns it back ON when activity resumes, and
turns it OFF on OS sleep/hibernate. Built on .NET 10 Windows Forms with the Generic Host.

## Platform

- **Windows-only.** Uses WinForms and Win32 P/Invoke (`user32.dll` `GetLastInputInfo`),
  so builds/publishes must run on Windows. CI uses `windows-latest`.
- Target framework: `net10.0-windows`. C# `LangVersion` 14, `Nullable` enabled,
  `ImplicitUsings` enabled.

## Development commands

The solution file is `IdlingLightManager.slnx` (XML-based solution format).

- Restore: `dotnet restore IdlingLightManager.slnx`
- Build (Release): `dotnet build IdlingLightManager.slnx /p:Configuration=Release`
- Publish (self-contained single-file win-x64 → `publish/`):
  `dotnet publish IdlingLightManager.slnx -p:PublishProfile=Publish`
- Format check (what CI enforces):
  `dotnet format IdlingLightManager.slnx --verify-no-changes --severity warn`
- Auto-fix formatting: `dotnet format IdlingLightManager.slnx`

There are **no automated tests**. Verify changes by building and running the app on
Windows (tray icon appears; check the ListView log window and the `logs/app-*.log` file).

## Architecture

- `IdlingLightManager/Program.cs` — entry point. Wires up the Generic Host with the
  WinForms message loop, registers services in DI, binds and validates options, and logs
  unhandled thread/domain exceptions. The host runs in the background while
  `Application.Run(tray)` drives the UI.
- `IdlingLightManager/Services/IdleDetectionService.cs` — `BackgroundService` that runs
  four parallel loops: fast check (fast ON on resume), slow check (idle→OFF), periodic
  resend (re-sends OFF as insurance), and a power-event loop (Suspend/Resume). State
  transitions are serialized by a `SemaphoreSlim` (`_stateLock`); a cooldown window after
  turning OFF suppresses spurious ON caused by wireless-device reconnects.
- `IdlingLightManager/Services/LightControlService.cs` — posts `{ "state": bool }` to the
  IoT API. Injected via `IHttpClientFactory` with a standard resilience handler; HTTP and
  URI errors are logged, not rethrown.
- `IdlingLightManager/Interop/Win32.cs` — `GetLastInputInfo` P/Invoke; computes idle time
  with a `uint` cast to survive the 49.7-day `TickCount` wraparound.
- `IdlingLightManager/Models/` — options records (`IdleDetectionOptions`,
  `LightControlOptions`) bound from configuration and validated on start.
- `IdlingLightManager/UI/` — `TrayApplicationContext` (tray residence, starts the form
  hidden), `MainForm` (log view + manual control), and `Sinks/ListViewSink.cs` (Serilog
  sink that renders into a `ListView`, capped at 200 rows, marshaling to the UI thread).

## Configuration and secrets

- `IdlingLightManager/appsettings.json` holds defaults and **placeholders**
  (`YOUR_DEVICE_ID`, `YOUR_API_TOKEN`) — never commit real credentials here.
- Real credentials go in `appsettings.local.json` (git-ignored, loaded as an optional
  overlay in `Program.cs`). Keep `DeviceId`/`ApiToken`/`ApiBaseUrl` out of version control.
- `LightControl` options are validated on start (`DeviceId` and `ApiToken` must be set), so
  the app fails fast if credentials are missing.

## Coding conventions

- **Comments and log/error messages are written in Japanese** (existing project
  convention). Match the surrounding style; keep XML doc comments in Japanese.
- **StyleCop.Analyzers is enforced** with full documentation rules — every member,
  including `private` members and fields, requires an XML `///` doc comment.
  `EnforceCodeStyleInBuild` is on, so style violations surface at build time.
- Line endings are **CRLF**, 4-space indentation, final newline required (`.editorconfig`).
- Logging uses the `[LoggerMessage]` source generator (`static partial` methods), not
  string interpolation into loggers.
- Use `ConfigureAwait(false)` on awaits in services (consistent with existing code).
- Types instantiated only via DI/config binding are `internal sealed` and suppress CA1812
  with a justification; follow that pattern rather than removing the analyzer suppression.
- When accessing shared idle-state fields, only do so while holding `_stateLock`; several
  helpers document this precondition — preserve it.

## Documentation update rules

- Adding or renaming a configuration option: update `appsettings.json`, the matching
  options class in `Models/`, and this file if it changes user-facing behavior.
- Changing build/publish commands or project layout: update the relevant section above.

## Commits

- Follow [Conventional Commits](https://www.conventionalcommits.org/). The Release
  workflow derives the semantic version and changelog from commit types
  (`feat`→minor, `fix`→patch, etc.), so the prefix matters.
- Commit descriptions follow existing history (Japanese).
