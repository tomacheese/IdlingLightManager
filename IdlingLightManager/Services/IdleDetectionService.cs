using System.Threading.Channels;
using IdlingLightManager.Interop;
using IdlingLightManager.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace IdlingLightManager.Services;

/// <summary>
/// Background service that monitors mouse/keyboard idle time and controls the light according to the threshold.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class IdleDetectionService(
    LightControlService lightControl,
    IOptions<IdleDetectionOptions> opts,
    ILogger<IdleDetectionService> logger) : BackgroundService
{
    /// <summary>Light control service.</summary>
    private readonly LightControlService _lightControl = lightControl;

    /// <summary>Idle detection options.</summary>
    private readonly IdleDetectionOptions _opts = opts.Value;

    /// <summary>Logger.</summary>
    private readonly ILogger<IdleDetectionService> _logger = logger;

    /// <summary>Semaphore that serializes state transitions and prevents the parallel loops from sending duplicate API calls.</summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>Current light state: <c>true</c> = ON, <c>false</c> = OFF.</summary>
    private volatile bool _isLightOn = true;

    /// <summary>
    /// Time at which the light was turned OFF; the start of the cooldown window.
    /// Access only while holding <see cref="_stateLock"/>.
    /// </summary>
    private DateTime? _lightOffAt;

    /// <summary>
    /// Queue that processes OS power state changes (Suspend/Resume) serially in arrival order.
    /// Preserves the firing order of <see cref="SystemEvents.PowerModeChanged"/> and is drained by <see cref="RunPowerEventLoopAsync"/>.
    /// </summary>
    private readonly Channel<PowerModes> _powerEventChannel = Channel.CreateUnbounded<PowerModes>(
        new UnboundedChannelOptions { SingleReader = true });

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to detect OS sleep/hibernate entry and resume
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        try
        {
            await _lightControl.SetLightStateAsync(true, stoppingToken).ConfigureAwait(false);

            using var fastTimer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.CheckIntervalSeconds));

            using var slowTimer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.SlowCheckIntervalSeconds));

            // Periodic resend timer (insurance if the light is turned ON unintentionally while idle)
            using var resendTimer = new PeriodicTimer(TimeSpan.FromMinutes(_opts.PeriodicResendIntervalMinutes));

            // Run the loops in parallel
            await Task.WhenAll(
                RunFastCheckAsync(fastTimer, stoppingToken),
                RunSlowCheckAsync(slowTimer, stoppingToken),
                RunResendAsync(resendTimer, stoppingToken),
                RunPowerEventLoopAsync(stoppingToken)).ConfigureAwait(false);
        }
        finally
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _powerEventChannel.Writer.TryComplete();
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Turn the light OFF on service stop if it is ON
            if (_isLightOn)
            {
                LogStoppingService(_logger);
                await TurnOffLightAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateLock.Release();
        }

        await base.StopAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _stateLock.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Fast check loop. Quickly detects resumed activity and turns the light ON.
    /// </summary>
    /// <param name="timer">The <see cref="PeriodicTimer"/> for the fast check.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RunFastCheckAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                TimeSpan idleTime = Win32.GetIdleTime();
                var threshold = TimeSpan.FromSeconds(_opts.ThresholdSeconds);

                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // Turn the light ON if idle time is below the threshold and the light is OFF
                    if (idleTime < threshold && !_isLightOn)
                    {
                        if (!HasNewInputAfterCooldown(idleTime))
                        {
                            DateTime cooldownEnd = _lightOffAt!.Value + TimeSpan.FromSeconds(_opts.CooldownSeconds);
                            LogOnSuppressed(_logger, cooldownEnd, DateTime.UtcNow - idleTime);
                        }
                        else
                        {
                            LogActivityDetectedFast(_logger, idleTime);
                            await _lightControl.SetLightStateAsync(true, ct).ConfigureAwait(false);
                            _isLightOn = true;
                            _lightOffAt = null;
                        }
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException derives from OperationCanceledException, so one catch covers both
            LogFastCheckCancelled(_logger);
        }
    }

    /// <summary>
    /// Slow check loop. Detects the idle threshold being exceeded and resumed activity, and switches the light state.
    /// </summary>
    /// <param name="timer">The <see cref="PeriodicTimer"/> for the slow check.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RunSlowCheckAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                TimeSpan idleTime = Win32.GetIdleTime();
                var threshold = TimeSpan.FromSeconds(_opts.ThresholdSeconds);

                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (idleTime >= threshold && _isLightOn)
                    {
                        // Turn the light OFF if idle time is at or above the threshold and the light is ON
                        LogIdleThresholdExceeded(_logger, idleTime);
                        await TurnOffLightAsync(ct).ConfigureAwait(false);
                    }
                    else if (idleTime < threshold && !_isLightOn)
                    {
                        // Turn the light ON if idle time is below the threshold and the light is OFF
                        if (!HasNewInputAfterCooldown(idleTime))
                        {
                            DateTime cooldownEnd = _lightOffAt!.Value + TimeSpan.FromSeconds(_opts.CooldownSeconds);
                            LogOnSuppressed(_logger, cooldownEnd, DateTime.UtcNow - idleTime);
                        }
                        else
                        {
                            LogActivityDetectedSlow(_logger, idleTime);
                            await _lightControl.SetLightStateAsync(true, ct).ConfigureAwait(false);
                            _isLightOn = true;
                            _lightOffAt = null;
                        }
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException derives from OperationCanceledException, so one catch covers both
            LogSlowCheckCancelled(_logger);
        }
    }

    /// <summary>
    /// Periodic resend loop. Re-sends OFF in case the light was turned ON unintentionally while idle.
    /// </summary>
    /// <param name="timer">The <see cref="PeriodicTimer"/> for the periodic resend.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RunResendAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (!_isLightOn)
                    {
                        LogPeriodicResend(_logger);
                        await _lightControl.SetLightStateAsync(false, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException derives from OperationCanceledException, so one catch covers both
            LogResendCancelled(_logger);
        }
    }

    /// <summary>
    /// Handler for OS power state changes (entering or resuming from sleep/hibernate).
    /// To preserve firing order, it only enqueues the event and leaves processing to <see cref="RunPowerEventLoopAsync"/>.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The power mode change event data.</param>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                // Log only if enqueueing succeeded (during shutdown the queue may already be
                // completed, in which case the event is never processed)
                if (_powerEventChannel.Writer.TryWrite(PowerModes.Suspend))
                    LogSystemSuspending(_logger);
                break;

            case PowerModes.Resume:
                if (_powerEventChannel.Writer.TryWrite(PowerModes.Resume))
                    LogSystemResumed(_logger);
                break;

            case PowerModes.StatusChange:
                // Ignored: notifications such as battery status changes are unrelated to sleep/hibernate
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Loop that processes power state events (sleep entry and resume) serially in arrival order.
    /// Processing the events enqueued by <see cref="OnPowerModeChanged"/> in FIFO order
    /// prevents <see cref="_lightOffAt"/> from being overwritten incorrectly by suspend and resume being handled out of order.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RunPowerEventLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (PowerModes mode in _powerEventChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (mode == PowerModes.Suspend)
                        await HandleSuspendAsync(ct).ConfigureAwait(false);
                    else if (mode == PowerModes.Resume)
                        await HandleResumeAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // A failure on one event must not stop the loop; keep processing later events
                    LogPowerEventHandlingError(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException derives from OperationCanceledException, so one catch covers both
            LogPowerEventLoopCancelled(_logger);
        }
    }

    /// <summary>
    /// Turns the light OFF when entering sleep/hibernate.
    /// Turning it OFF immediately, even below the idle threshold, avoids leaving the light on when going to bed, for example.
    /// If it is already OFF, neither the state nor the cooldown start is changed.
    /// </summary>
    /// <param name="ct">The cancellation token. Applies only to waiting for the lock, not to the OFF API call itself.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleSuspendAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isLightOn)
            {
                LogSuspendTurningOff(_logger);

                // The OS sleep transition proceeds independently of the app's stop token, so the
                // OFF API call must not be aborted by it. This avoids leaving the light on when
                // the machine sleeps at the same time the app exits.
                await TurnOffLightAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Restarts the cooldown when resuming from sleep/hibernate.
    /// Right after resume, device reconnects and the like can cause false detections,
    /// so the light is turned ON only after real new input following the cooldown period.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleResumeAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Use the resume time as the new cooldown start
            _lightOffAt = DateTime.UtcNow;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Turns the light OFF and updates the state and the cooldown start.
    /// Call only while holding <see cref="_stateLock"/>.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task TurnOffLightAsync(CancellationToken ct)
    {
        await _lightControl.SetLightStateAsync(false, ct).ConfigureAwait(false);
        _isLightOn = false;
        _lightOffAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns whether new input occurred after the cooldown ended. Used as the condition for allowing an ON transition.
    /// Always returns <see langword="false"/> during the cooldown. Even after the cooldown has ended,
    /// it returns <see langword="false"/> if the last input predates the cooldown end (e.g. a device reconnect around the time the light was turned OFF).
    /// Call only while holding <see cref="_stateLock"/>.
    /// </summary>
    /// <param name="idleTime">The current idle time.</param>
    /// <returns><see langword="true"/> if new input occurred after the cooldown ended.</returns>
    private bool HasNewInputAfterCooldown(TimeSpan idleTime)
    {
        if (!_lightOffAt.HasValue) return true;
        DateTime cooldownEnd = _lightOffAt.Value + TimeSpan.FromSeconds(_opts.CooldownSeconds);
        DateTime lastInputAt = DateTime.UtcNow - idleTime;
        return lastInputAt > cooldownEnd;
    }

    /// <summary>Logs that resumed activity was detected by the fast check.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "操作が検知されました（高頻度）。照明を ON にします。IdleTime={IdleTime}")]
    private static partial void LogActivityDetectedFast(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>Logs that the idle threshold was exceeded.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "アイドル閾値を超過しました。照明を OFF にします。IdleTime={IdleTime}")]
    private static partial void LogIdleThresholdExceeded(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>Logs that resumed activity was detected by the slow check.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "操作が検知されました（低頻度）。照明を ON にします。IdleTime={IdleTime}")]
    private static partial void LogActivityDetectedSlow(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>Logs that a periodic resend is being performed.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "定期再送: 照明を OFF で再送します。")]
    private static partial void LogPeriodicResend(ILogger<IdleDetectionService> logger);

    /// <summary>Logs the cancellation of the fast check loop.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "高頻度チェックループがキャンセルされました（正常終了）。")]
    private static partial void LogFastCheckCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>Logs the cancellation of the slow check loop.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "低頻度チェックループがキャンセルされました（正常終了）。")]
    private static partial void LogSlowCheckCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>Logs the cancellation of the periodic resend loop.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "定期再送ループがキャンセルされました（正常終了）。")]
    private static partial void LogResendCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>Logs that turning the light ON was suppressed because of the cooldown or no new input after it.</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "照明 ON を抑制します（クールダウン中またはクールダウン後の新規入力なし）。CooldownEnd={CooldownEnd}, LastInputAt={LastInputAt}")]
    private static partial void LogOnSuppressed(ILogger<IdleDetectionService> logger, DateTime cooldownEnd, DateTime lastInputAt);

    /// <summary>Logs that OFF is sent because the service is stopping.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "サービスを停止します。照明を OFF にします。")]
    private static partial void LogStoppingService(ILogger<IdleDetectionService> logger);

    /// <summary>Logs that the OS is entering sleep/hibernate.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "OS がスリープ／休止状態へ移行します。")]
    private static partial void LogSystemSuspending(ILogger<IdleDetectionService> logger);

    /// <summary>Logs that the light is turned OFF because of the sleep/hibernate transition.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "スリープ移行に伴い照明を OFF にします。")]
    private static partial void LogSuspendTurningOff(ILogger<IdleDetectionService> logger);

    /// <summary>Logs that the OS resumed from sleep/hibernate.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "OS がスリープ／休止状態から復帰しました。クールダウンを再開します。")]
    private static partial void LogSystemResumed(ILogger<IdleDetectionService> logger);

    /// <summary>Logs an exception raised while handling a power state change.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "電源状態変化ハンドラーの処理中にエラーが発生しました。")]
    private static partial void LogPowerEventHandlingError(ILogger<IdleDetectionService> logger, Exception exception);

    /// <summary>Logs the cancellation of the power event loop.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "電源状態イベント処理ループがキャンセルされました（正常終了）。")]
    private static partial void LogPowerEventLoopCancelled(ILogger<IdleDetectionService> logger);
}
