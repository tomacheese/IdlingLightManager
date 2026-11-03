using System.Diagnostics.CodeAnalysis;

namespace IdlingLightManager.Models;

/// <summary>
/// Options for idle detection.
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by configuration binding.")]
internal sealed class IdleDetectionOptions
{
    /// <summary>Idle threshold in seconds. The light is turned OFF once idle time reaches it.</summary>
    public int ThresholdSeconds { get; init; } = 300;

    /// <summary>Fast check interval in seconds. Keeps the response to resumed activity quick.</summary>
    public int CheckIntervalSeconds { get; init; } = 5;

    /// <summary>Slow check interval in seconds. Used to detect that the idle threshold was exceeded.</summary>
    public int SlowCheckIntervalSeconds { get; init; } = 30;

    /// <summary>Interval in minutes for re-sending OFF while idle, as insurance against the light being turned ON unintentionally.</summary>
    public int PeriodicResendIntervalMinutes { get; init; } = 10;

    /// <summary>Cooldown in seconds after turning the light OFF during which ON is suppressed, to avoid false detections such as wireless device reconnects.</summary>
    public int CooldownSeconds { get; init; } = 60;
}
