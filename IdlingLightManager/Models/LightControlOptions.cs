using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace IdlingLightManager.Models;

/// <summary>
/// Options for the light control API.
/// </summary>
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by configuration binding.")]
internal sealed class LightControlOptions
{
    /// <summary>Base URL of the IoT API. Must end with a trailing slash because <see cref="DeviceId"/> is resolved relative to it.</summary>
    [Required]
    public Uri ApiBaseUrl { get; init; } = new Uri("about:blank");

    /// <summary>ID of the device to control.</summary>
    [Required]
    public string DeviceId { get; init; } = string.Empty;

    /// <summary>Bearer authentication token.</summary>
    [Required]
    public string ApiToken { get; init; } = string.Empty;
}
