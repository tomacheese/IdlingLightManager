using System.Diagnostics.CodeAnalysis;

namespace IdlingLightManager.Models;

/// <summary>
/// アイドル検知に関する設定オプション。
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by configuration binding.")]
internal sealed class IdleDetectionOptions
{
    /// <summary>アイドル判定の閾値（秒）。この時間を超えたら照明を OFF にする。</summary>
    public int ThresholdSeconds { get; init; } = 300;

    /// <summary>高頻度チェック間隔（秒）。操作復帰の応答性を高めるために使用する。</summary>
    public int CheckIntervalSeconds { get; init; } = 5;

    /// <summary>低頻度チェック間隔（秒）。アイドル超過を判定する際に使用する。</summary>
    public int SlowCheckIntervalSeconds { get; init; } = 30;

    /// <summary>アイドル中の定期再送間隔（分）。照明が意図せず ON になった場合の保険。</summary>
    public int PeriodicResendIntervalMinutes { get; init; } = 10;
}
