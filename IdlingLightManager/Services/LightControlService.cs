using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using IdlingLightManager.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdlingLightManager.Services;

/// <summary>
/// IoT API を通じて照明の ON/OFF を制御するサービス。
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed partial class LightControlService(
    HttpClient http,
    IOptions<LightControlOptions> opts,
    ILogger<LightControlService> logger)
{
    /// <summary>HTTP クライアント。</summary>
    private readonly HttpClient _http = http;

    /// <summary>照明制御オプション。</summary>
    private readonly IOptions<LightControlOptions> _opts = opts;

    /// <summary>ロガー。</summary>
    private readonly ILogger<LightControlService> _logger = logger;

    /// <summary>
    /// 照明の状態を設定する。
    /// </summary>
    /// <param name="state"><c>true</c> で ON、<c>false</c> で OFF。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>非同期操作を表すタスク。</returns>
    public async Task SetLightStateAsync(bool state, CancellationToken ct = default)
    {
        var deviceId = _opts.Value.DeviceId;

        // リクエストボディを JSON にシリアライズする
        var payload = JsonSerializer.Serialize(new { state });

        LogSendingLightState(_logger, deviceId, state);

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync(new Uri(deviceId, UriKind.Relative), content, ct).ConfigureAwait(false);

            // レスポンスのステータスコードとボディをログ出力する
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                LogLightStateSent(_logger, (int)response.StatusCode, body);
            else
                LogHttpResponseError(_logger, (int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            // HTTP 通信エラーはログに記録するが再スローしない
            LogHttpError(_logger, ex, deviceId, state);
        }
        catch (UriFormatException ex)
        {
            // DeviceId の URI 形式が不正な場合はログに記録するが再スローしない
            LogUriFormatError(_logger, ex, deviceId);
        }
    }

    /// <summary>照明状態の送信開始をログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "照明状態を送信します: DeviceId={DeviceId}, State={State}")]
    private static partial void LogSendingLightState(ILogger<LightControlService> logger, string deviceId, bool state);

    /// <summary>照明状態の送信完了をログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "照明状態の送信が完了しました: StatusCode={StatusCode}, Body={Body}")]
    private static partial void LogLightStateSent(ILogger<LightControlService> logger, int statusCode, string body);

    /// <summary>照明状態の送信がエラーレスポンスを返したことをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "照明状態の送信がエラーレスポンスを返しました: StatusCode={StatusCode}, Body={Body}")]
    private static partial void LogHttpResponseError(ILogger<LightControlService> logger, int statusCode, string body);

    /// <summary>照明状態の送信中に発生した HTTP エラーをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "照明状態の送信中に HTTP エラーが発生しました: DeviceId={DeviceId}, State={State}")]
    private static partial void LogHttpError(ILogger<LightControlService> logger, Exception exception, string deviceId, bool state);

    /// <summary>DeviceId の URI 形式エラーをログ出力する。</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "DeviceId の URI 形式が不正です: DeviceId={DeviceId}")]
    private static partial void LogUriFormatError(ILogger<LightControlService> logger, Exception exception, string deviceId);
}
