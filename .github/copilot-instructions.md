# Copilot code review instructions

IdlingLightManager is a Windows-only .NET 10 Windows Forms tray app (C#) that monitors
keyboard/mouse idle time via Win32 `GetLastInputInfo` and toggles an IoT light over an
HTTP API. It uses the Generic Host, DI, Serilog, and `IHttpClientFactory`.

## Review priorities

- **Secrets**: flag any real credentials committed to `appsettings.json`. Real values
  belong only in the git-ignored `appsettings.local.json`; `appsettings.json` must keep
  placeholders (`YOUR_DEVICE_ID`, `YOUR_API_TOKEN`).
- **Concurrency correctness**: `IdleDetectionService` runs four parallel loops that share
  mutable state (`_isLightOn`, `_lightOffAt`). All reads/writes of that state must happen
  while holding `_stateLock`. Flag new state access that skips the lock, and flag
  `await`s made while holding the lock that could deadlock the single-permit semaphore.
- **CancellationToken handling**: sleep/suspend light-off must complete regardless of the
  app stop token — `HandleSuspendAsync` intentionally calls the OFF path with
  `CancellationToken.None`. Do not flag that as a missing-token bug.
- **HTTP resilience**: `LightControlService` deliberately logs and swallows
  `HttpRequestException`/`UriFormatException` instead of rethrowing, so a transient device
  failure does not crash the tray app. Treat this as intended, not a swallowed-exception bug.
- **UI-thread marshaling**: WinForms controls must be touched on the UI thread. Verify new
  `ListView`/form access goes through `InvokeRequired`/`BeginInvoke` and guards against
  disposed/handle-not-created controls (see `ListViewSink`, `TrayApplicationContext`).
- **Time/tick math**: idle time relies on a `uint` cast to handle `TickCount`'s 49.7-day
  wraparound — flag changes that reintroduce signed overflow.

## Conventions enforced here

- StyleCop.Analyzers is active with full documentation rules: **every** member, including
  `private` members and fields, needs an XML `///` doc comment. Flag missing docs on new
  members. `EnforceCodeStyleInBuild` is on and CI runs
  `dotnet format --verify-no-changes`, so formatting deviations break the build.
- Comments and log/error messages are written in **Japanese** by convention — do not ask
  for them to be translated to English.
- Logging must use the `[LoggerMessage]` source generator (`static partial` methods), not
  string interpolation passed to `ILogger`.
- CRLF line endings, 4-space indent, final newline (see `.editorconfig`). Nullable is
  enabled; flag new nullable-warning suppressions that lack justification.

## Known non-issues (do not flag)

- `internal sealed` classes carrying `[SuppressMessage(... "CA1812" ...)]` — these are
  instantiated via DI or config binding, so the "uninstantiated" warning is a false positive.
- No unit tests exist; do not request test coverage for small changes. This is a UI/OS-bound
  desktop app verified manually.
- `Serilog.Sinks.File` writes to `logs/app-*.log` with daily rolling by design.
