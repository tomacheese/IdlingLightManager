using System.Diagnostics.CodeAnalysis;
using IdlingLightManager.Services;
using IdlingLightManager.UI.Sinks;
using Microsoft.Extensions.Logging;

namespace IdlingLightManager.UI;

/// <summary>
/// アプリケーションのメインフォーム。ログ表示と照明制御ボタンを提供する。
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class MainForm : Form
{
    /// <summary>照明制御サービス。</summary>
    private readonly LightControlService _lightControlService;

    /// <summary>ListView へログを出力するシンク。</summary>
    private readonly ListViewSink _listViewSink;

    /// <summary>ロガー。</summary>
    private readonly ILogger<MainForm> _logger;

    /// <summary>
    /// <see cref="MainForm"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="lightControlService">照明制御サービス。</param>
    /// <param name="listViewSink">ListView へログを出力するシンク。</param>
    /// <param name="logger">ロガー。</param>
    public MainForm(
        LightControlService lightControlService,
        ListViewSink listViewSink,
        ILogger<MainForm> logger)
    {
        _lightControlService = lightControlService;
        _listViewSink = listViewSink;
        _logger = logger;

        InitializeComponent();
    }

    /// <summary>
    /// フォームロード時に ListViewSink のターゲットを設定する。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMainFormLoad(object? sender, EventArgs e) => _listViewSink.SetTarget(LogView);

    /// <summary>
    /// PCStart ボタンクリック時に照明を ON にする。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnPcStartClick(object? sender, EventArgs e)
    {
        // fire-and-forget で実行し、例外はログに記録する
        Task.Run(async () =>
        {
            try
            {
                await _lightControlService.SetLightStateAsync(true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPcStartError(_logger, ex);
            }
        });
    }

    /// <summary>
    /// PCStop ボタンクリック時に照明を OFF にする。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnPcStopClick(object? sender, EventArgs e)
    {
        // fire-and-forget で実行し、例外はログに記録する
        Task.Run(async () =>
        {
            try
            {
                await _lightControlService.SetLightStateAsync(false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogPcStopError(_logger, ex);
            }
        });
    }

    /// <summary>
    /// Exit ボタンクリック時にアプリケーションを終了する。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnExitClick(object? sender, EventArgs e) => Application.Exit();

    /// <summary>
    /// トレイメニューの「開く」クリック時にフォームを前面に表示する。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMenuItemOpenClick(object? sender, EventArgs e)
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// タスクトレイアイコンのダブルクリック時にフォームを前面に表示する。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">マウスイベントデータ。</param>
    private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// フォームのサイズ変更時に最小化されたらタスクトレイに格納する。
    /// </summary>
    /// <param name="sender">イベント送信元オブジェクト。</param>
    /// <param name="e">イベントデータ。</param>
    private void OnMainFormResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    /// <summary>
    /// 照明の ON 操作中にエラーが発生した場合のログメッセージを出力する。
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明の ON 操作中にエラーが発生しました。")]
    private static partial void LogPcStartError(ILogger<MainForm> logger, Exception exception);

    /// <summary>
    /// 照明の OFF 操作中にエラーが発生した場合のログメッセージを出力する。
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明の OFF 操作中にエラーが発生しました。")]
    private static partial void LogPcStopError(ILogger<MainForm> logger, Exception exception);
}
