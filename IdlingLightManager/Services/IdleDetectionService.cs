using System.Threading.Channels;
using IdlingLightManager.Interop;
using IdlingLightManager.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace IdlingLightManager.Services;

/// <summary>
/// マウス・キーボードのアイドル時間を監視し、閾値に応じて照明を制御するバックグラウンドサービス。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class IdleDetectionService(
    LightControlService lightControl,
    IOptions<IdleDetectionOptions> opts,
    ILogger<IdleDetectionService> logger) : BackgroundService
{
    /// <summary>照明制御サービス。</summary>
    private readonly LightControlService _lightControl = lightControl;

    /// <summary>アイドル検知オプション。</summary>
    private readonly IdleDetectionOptions _opts = opts.Value;

    /// <summary>ロガー。</summary>
    private readonly ILogger<IdleDetectionService> _logger = logger;

    /// <summary>状態遷移の直列化用セマフォ。並列ループによる API 重複送信を防ぐ。</summary>
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>照明の現在の状態。true = ON、false = OFF。</summary>
    private volatile bool _isLightOn = true;

    /// <summary>
    /// 照明を OFF にした時刻。クールダウン期間の起点として使用する。
    /// <see cref="_stateLock"/> 保持中のみアクセスする。
    /// </summary>
    private DateTime? _lightOffAt;

    /// <summary>
    /// OS の電源状態変化（Suspend/Resume）を受信順に直列処理するためのキュー。
    /// <see cref="SystemEvents.PowerModeChanged"/> の発火順を保ったまま <see cref="RunPowerEventLoopAsync"/> で処理する。
    /// </summary>
    private readonly Channel<PowerModes> _powerEventChannel = Channel.CreateUnbounded<PowerModes>(
        new UnboundedChannelOptions { SingleReader = true });

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // OS のスリープ／休止状態への移行・復帰を検知するためにイベントを登録する
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        try
        {
            // 起動時に照明を ON にする
            await _lightControl.SetLightStateAsync(true, stoppingToken).ConfigureAwait(false);

            // 高頻度チェック用タイマー（操作復帰を素早く検知する）
            using var fastTimer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.CheckIntervalSeconds));

            // 低頻度チェック用タイマー（アイドル超過を判定する）
            using var slowTimer = new PeriodicTimer(TimeSpan.FromSeconds(_opts.SlowCheckIntervalSeconds));

            // 定期再送用タイマー（アイドル中に照明が意図せず ON になった場合の保険）
            using var resendTimer = new PeriodicTimer(TimeSpan.FromMinutes(_opts.PeriodicResendIntervalMinutes));

            // 4 つのループを並列実行する
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
            // サービス停止時に照明が ON であれば OFF にする
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
    /// 高頻度チェックループ。操作復帰（アイドル解除）を素早く検知して照明を ON にする。
    /// </summary>
    /// <param name="timer">高頻度チェック用の <see cref="PeriodicTimer"/>。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
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
                    // アイドル時間が閾値未満かつ照明が OFF の場合、照明を ON にする
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
            // TaskCanceledException は OperationCanceledException の派生型なので一括でキャッチする
            LogFastCheckCancelled(_logger);
        }
    }

    /// <summary>
    /// 低頻度チェックループ。アイドル超過および操作復帰を判定して照明状態を切り替える。
    /// </summary>
    /// <param name="timer">低頻度チェック用の <see cref="PeriodicTimer"/>。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
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
                        // アイドル時間が閾値以上かつ照明が ON の場合、照明を OFF にする
                        LogIdleThresholdExceeded(_logger, idleTime);
                        await TurnOffLightAsync(ct).ConfigureAwait(false);
                    }
                    else if (idleTime < threshold && !_isLightOn)
                    {
                        // アイドル時間が閾値未満かつ照明が OFF の場合、照明を ON にする
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
            // TaskCanceledException は OperationCanceledException の派生型なので一括でキャッチする
            LogSlowCheckCancelled(_logger);
        }
    }

    /// <summary>
    /// 定期再送ループ。アイドル中に照明が意図せず ON になっていた場合に OFF を再送する。
    /// </summary>
    /// <param name="timer">定期再送用の <see cref="PeriodicTimer"/>。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    private async Task RunResendAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    // 照明が OFF 状態のとき、念のため OFF を再送する
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
            // TaskCanceledException は OperationCanceledException の派生型なので一括でキャッチする
            LogResendCancelled(_logger);
        }
    }

    /// <summary>
    /// OS の電源状態変化（スリープ／休止状態への移行・復帰）を検知するハンドラー。
    /// 発火順を保つため、処理自体は行わずキューへ積んで <see cref="RunPowerEventLoopAsync"/> に委ねる。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">電源状態変化イベントデータ。</param>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                LogSystemSuspending(_logger);
                _powerEventChannel.Writer.TryWrite(PowerModes.Suspend);
                break;

            case PowerModes.Resume:
                LogSystemResumed(_logger);
                _powerEventChannel.Writer.TryWrite(PowerModes.Resume);
                break;

            case PowerModes.StatusChange:
                // バッテリー状態変化など、スリープ／休止状態と無関係な通知のため無視する
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 電源状態イベント（スリープ移行・復帰）を受信順に直列処理するループ。
    /// <see cref="OnPowerModeChanged"/> から積まれたイベントを FIFO で処理することで、
    /// 移行・復帰の処理順序が入れ替わることによる <see cref="_lightOffAt"/> の誤上書きを防ぐ。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
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
                    // 1 件の処理失敗でループ自体を止めず、以降のイベントも処理を継続する
                    LogPowerEventHandlingError(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // TaskCanceledException は OperationCanceledException の派生型なので一括でキャッチする
            LogPowerEventLoopCancelled(_logger);
        }
    }

    /// <summary>
    /// スリープ／休止状態への移行時に照明を OFF にする。
    /// アイドル閾値未到達でも即座に OFF にすることで、就寝時などの消灯漏れを防ぐ。
    /// すでに OFF の場合は状態を変更せず、クールダウンの起点も更新しない。
    /// </summary>
    /// <param name="ct">キャンセルトークン。ロック取得待ちにのみ適用され、消灯 API 呼び出し自体には適用しない。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    private async Task HandleSuspendAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isLightOn)
            {
                LogSuspendTurningOff(_logger);

                // OS のスリープ移行処理はアプリ自身の停止トークンとは無関係に進むため、
                // 消灯 API 呼び出しはアプリの停止トークンで中断せず完了させる
                // （就寝時などにアプリ終了と同時にスリープした場合の消灯漏れを防ぐ）
                await TurnOffLightAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// スリープ／休止状態からの復帰時にクールダウンを再開する。
    /// 復帰直後はデバイス再接続等による誤検知の可能性があるため、
    /// クールダウン期間を経た実際の新規入力を待って照明を ON にする。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    private async Task HandleResumeAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 復帰時点を新たなクールダウンの起点とする
            _lightOffAt = DateTime.UtcNow;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// 照明を OFF にし、状態とクールダウンの起点を更新する。
    /// <see cref="_stateLock"/> 保持中のみ呼び出すこと。
    /// </summary>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    private async Task TurnOffLightAsync(CancellationToken ct)
    {
        await _lightControl.SetLightStateAsync(false, ct).ConfigureAwait(false);
        _isLightOn = false;
        _lightOffAt = DateTime.UtcNow;
    }

    /// <summary>
    /// クールダウン終了後に新しい入力が発生したかどうかを返す。ON 遷移の許可条件として使用する。
    /// クールダウン期間中は常に <see langword="false"/> を返す。また、クールダウン終了後であっても、
    /// 最後の入力がクールダウン終了前のもの（照明 OFF 前後のデバイス再接続等）であれば <see langword="false"/> を返す。
    /// <see cref="_stateLock"/> 保持中のみ呼び出すこと。
    /// </summary>
    /// <param name="idleTime">現在のアイドル時間。</param>
    /// <returns>クールダウン終了後に新しい入力が発生していれば <see langword="true"/>。</returns>
    private bool HasNewInputAfterCooldown(TimeSpan idleTime)
    {
        if (!_lightOffAt.HasValue) return true;
        DateTime cooldownEnd = _lightOffAt.Value + TimeSpan.FromSeconds(_opts.CooldownSeconds);
        DateTime lastInputAt = DateTime.UtcNow - idleTime;
        return lastInputAt > cooldownEnd;
    }

    /// <summary>操作復帰（高頻度チェック）を検知したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "操作が検知されました（高頻度）。照明を ON にします。IdleTime={IdleTime}")]
    private static partial void LogActivityDetectedFast(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>アイドル閾値超過を検知したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "アイドル閾値を超過しました。照明を OFF にします。IdleTime={IdleTime}")]
    private static partial void LogIdleThresholdExceeded(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>操作復帰（低頻度チェック）を検知したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "操作が検知されました（低頻度）。照明を ON にします。IdleTime={IdleTime}")]
    private static partial void LogActivityDetectedSlow(ILogger<IdleDetectionService> logger, TimeSpan idleTime);

    /// <summary>定期再送を実行することをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "定期再送: 照明を OFF で再送します。")]
    private static partial void LogPeriodicResend(ILogger<IdleDetectionService> logger);

    /// <summary>高頻度チェックループのキャンセルをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "高頻度チェックループがキャンセルされました（正常終了）。")]
    private static partial void LogFastCheckCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>低頻度チェックループのキャンセルをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "低頻度チェックループがキャンセルされました（正常終了）。")]
    private static partial void LogSlowCheckCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>定期再送ループのキャンセルをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "定期再送ループがキャンセルされました（正常終了）。")]
    private static partial void LogResendCancelled(ILogger<IdleDetectionService> logger);

    /// <summary>クールダウン期間中または終了後の新規入力なしのため照明 ON を抑制したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "照明 ON を抑制します（クールダウン中またはクールダウン後の新規入力なし）。CooldownEnd={CooldownEnd}, LastInputAt={LastInputAt}")]
    private static partial void LogOnSuppressed(ILogger<IdleDetectionService> logger, DateTime cooldownEnd, DateTime lastInputAt);

    /// <summary>サービス停止時の照明 OFF 送信をログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "サービスを停止します。照明を OFF にします。")]
    private static partial void LogStoppingService(ILogger<IdleDetectionService> logger);

    /// <summary>OS がスリープ／休止状態へ移行することをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "OS がスリープ／休止状態へ移行します。")]
    private static partial void LogSystemSuspending(ILogger<IdleDetectionService> logger);

    /// <summary>スリープ／休止状態への移行に伴い照明を OFF にすることをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "スリープ移行に伴い照明を OFF にします。")]
    private static partial void LogSuspendTurningOff(ILogger<IdleDetectionService> logger);

    /// <summary>OS がスリープ／休止状態から復帰したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "OS がスリープ／休止状態から復帰しました。クールダウンを再開します。")]
    private static partial void LogSystemResumed(ILogger<IdleDetectionService> logger);

    /// <summary>電源状態変化ハンドラーの処理中に発生した例外をログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "電源状態変化ハンドラーの処理中にエラーが発生しました。")]
    private static partial void LogPowerEventHandlingError(ILogger<IdleDetectionService> logger, Exception exception);

    /// <summary>電源状態イベント処理ループのキャンセルをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "電源状態イベント処理ループがキャンセルされました（正常終了）。")]
    private static partial void LogPowerEventLoopCancelled(ILogger<IdleDetectionService> logger);
}
