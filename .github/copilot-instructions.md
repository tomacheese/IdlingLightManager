# Copilot code review instructions

Project overview, commands and conventions are in [`AGENTS.md`](../AGENTS.md). Write review comments in English.

## Review priorities

- **Secrets**: flag any real credentials committed to `appsettings.json`; it must keep the placeholders (`YOUR_DEVICE_ID`, `YOUR_API_TOKEN`). Real values belong only in the git-ignored `appsettings.local.json`.
- **Concurrency**: `IdleDetectionService` runs parallel loops sharing `_isLightOn` and `_lightOffAt`. Flag any access that skips `_stateLock`, and any `await` while holding it that could deadlock the single-permit semaphore.
- **UI-thread marshaling**: flag new `ListView`/form access that bypasses `InvokeRequired`/`BeginInvoke` or does not guard against disposed/handle-not-created controls (see `ListViewSink`, `TrayApplicationContext`).
- **Tick math**: idle time relies on a `uint` cast for the 49.7-day `TickCount` wraparound; flag changes that reintroduce signed overflow.
- **Documentation**: every member, including `private` ones, needs an accurate XML `///` doc comment (StyleCop). Flag missing docs on new members. CI runs `dotnet format --verify-no-changes`, so formatting deviations fail the build. Flag new nullable-warning suppressions without justification.
- Logging must use the `[LoggerMessage]` source generator, not string interpolation passed to `ILogger`.

## Known non-issues (do not flag)

- `HandleSuspendAsync` calls the OFF path with `CancellationToken.None` on purpose.
- `LightControlService` logs and swallows `HttpRequestException`/`UriFormatException` on purpose.
- `internal sealed` classes with `[SuppressMessage(... "CA1812" ...)]` are instantiated via DI or config binding.
- Existing Japanese log, validation and UI strings are intentional runtime text.
- No unit tests exist; do not request test coverage for small changes (UI/OS-bound desktop app verified manually).
- `Serilog.Sinks.File` writes `logs/app-*.log` with daily rolling by design.
