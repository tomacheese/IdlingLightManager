using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace IdlingLightManager.Models;

/// <summary>
/// 照明制御 API に関する設定オプション。
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by configuration binding.")]
internal sealed class LightControlOptions
{
    /// <summary>IoT API のベース URL。末尾スラッシュを含む。</summary>
    [Required]
    public Uri ApiBaseUrl { get; init; } = new Uri("about:blank");

    /// <summary>制御対象のデバイス ID。</summary>
    [Required]
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>Bearer 認証トークン。</summary>
    [Required]
    public string ApiToken { get; init; } = string.Empty;
}
