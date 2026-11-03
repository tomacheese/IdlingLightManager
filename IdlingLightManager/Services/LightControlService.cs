using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using IdlingLightManager.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdlingLightManager.Services;

/// <summary>
/// Service that turns the light ON/OFF through the IoT API.
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class LightControlService(
    HttpClient http,
    IOptions<LightControlOptions> opts,
    ILogger<LightControlService> logger)
{
    /// <summary>HTTP client.</summary>
    private readonly HttpClient _http = http;

    /// <summary>Light control options.</summary>
    private readonly IOptions<LightControlOptions> _opts = opts;

    /// <summary>Logger.</summary>
    private readonly ILogger<LightControlService> _logger = logger;

    /// <summary>
    /// Sets the light state.
    /// </summary>
    /// <param name="state"><c>true</c> to turn ON, <c>false</c> to turn OFF.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SetLightStateAsync(bool state, CancellationToken ct = default)
    {
        var deviceId = _opts.Value.DeviceId;

        var payload = JsonSerializer.Serialize(new { state });

        LogSendingLightState(_logger, deviceId, state);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync(new Uri(deviceId, UriKind.Relative), content, ct).ConfigureAwait(false);

            // Log the response status code and body
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                LogLightStateSent(_logger, (int)response.StatusCode, body);
            else
                LogHttpResponseError(_logger, (int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            // HTTP errors are logged but not rethrown, so a transient device failure does not crash the tray app
            LogHttpError(_logger, ex, deviceId, state);
        }
        catch (UriFormatException ex)
        {
            // An invalid DeviceId URI is logged but not rethrown
            LogUriFormatError(_logger, ex, deviceId);
        }
    }

    /// <summary>Logs that sending the light state is starting.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "照明状態を送信します: DeviceId={DeviceId}, State={State}")]
    private static partial void LogSendingLightState(ILogger<LightControlService> logger, string deviceId, bool state);

    /// <summary>Logs that the light state was sent.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "照明状態の送信が完了しました: StatusCode={StatusCode}, Body={Body}")]
    private static partial void LogLightStateSent(ILogger<LightControlService> logger, int statusCode, string body);

    /// <summary>Logs that sending the light state returned an error response.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "照明状態の送信がエラーレスポンスを返しました: StatusCode={StatusCode}, Body={Body}")]
    private static partial void LogHttpResponseError(ILogger<LightControlService> logger, int statusCode, string body);

    /// <summary>Logs an HTTP error that occurred while sending the light state.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明状態の送信中に HTTP エラーが発生しました: DeviceId={DeviceId}, State={State}")]
    private static partial void LogHttpError(ILogger<LightControlService> logger, Exception exception, string deviceId, bool state);

    /// <summary>Logs an invalid DeviceId URI format.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "DeviceId の URI 形式が不正です: DeviceId={DeviceId}")]
    private static partial void LogUriFormatError(ILogger<LightControlService> logger, Exception exception, string deviceId);
}
