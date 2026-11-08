#region

using Microsoft.Extensions.Compliance.Classification;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// Data classifications owned by the GameApi diagnostic sanitizer.
/// </summary>
internal static class GameApiDataClassifications
{
    /// <summary>
    /// Identifiers, credentials and free text that must never reach a log sink.
    /// </summary>
    public static readonly DataClassification Sensitive = new("MehrakGameApi", "Sensitive");
}

/// <summary>
/// Marks a response DTO member whose value is replaced by <see cref="GameApiLog.RedactedMarker"/> before a diagnostic
/// body is written. All other public members of known response models stay visible by default.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal sealed class GameApiSensitiveAttribute : DataClassificationAttribute
{
    public GameApiSensitiveAttribute() : base(GameApiDataClassifications.Sensitive)
    {
    }
}
