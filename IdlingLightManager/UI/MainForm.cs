using System.Diagnostics.CodeAnalysis;
using IdlingLightManager.Services;
using IdlingLightManager.UI.Sinks;
using Microsoft.Extensions.Logging;

namespace IdlingLightManager.UI;

/// <summary>
/// Main form of the application. Provides the log view and light control buttons.
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class MainForm : Form
{
    /// <summary>Light control service.</summary>
    private readonly LightControlService _lightControlService;

    /// <summary>Sink that writes logs to the ListView.</summary>
    private readonly ListViewSink _listViewSink;

    /// <summary>Logger.</summary>
    private readonly ILogger<MainForm> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainForm"/> class.
    /// </summary>
    /// <param name="lightControlService">The light control service.</param>
    /// <param name="listViewSink">The sink that writes logs to the ListView.</param>
    /// <param name="logger">The logger.</param>
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
    /// Sets the <see cref="ListViewSink"/> target when the form loads.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnMainFormLoad(object? sender, EventArgs e) => _listViewSink.SetTarget(LogView);

    /// <summary>
    /// Turns the light ON when the PCStart button is clicked.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnPcStartClick(object? sender, EventArgs e)
    {
        // Fire-and-forget; exceptions are logged
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
    /// Turns the light OFF when the PCStop button is clicked.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnPcStopClick(object? sender, EventArgs e)
    {
        // Fire-and-forget; exceptions are logged
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
    /// Exits the application when the Exit button is clicked.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnExitClick(object? sender, EventArgs e) => Application.Exit();

    /// <summary>
    /// Brings the form to the front when the tray menu "Open" item is clicked.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnMenuItemOpenClick(object? sender, EventArgs e)
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Brings the form to the front when the tray icon is double-clicked.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The mouse event data.</param>
    private void OnNotifyIconMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Hides the form to the tray when it is minimized.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The event data.</param>
    private void OnMainFormResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    /// <summary>
    /// Logs an error that occurred while turning the light ON.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明の ON 操作中にエラーが発生しました。")]
    private static partial void LogPcStartError(ILogger<MainForm> logger, Exception exception);

    /// <summary>
    /// Logs an error that occurred while turning the light OFF.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明の OFF 操作中にエラーが発生しました。")]
    private static partial void LogPcStopError(ILogger<MainForm> logger, Exception exception);
}
