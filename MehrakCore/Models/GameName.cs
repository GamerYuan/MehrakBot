#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameName
{
    Genshin,
    HonkaiStarRail,
    ZenlessZoneZero,
    HonkaiImpact3,
    TearsOfThemis
}
