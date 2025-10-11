using System.Text.Json.Serialization;

namespace Mehrak.Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Game
{
    Genshin,
    HonkaiStarRail,
    ZenlessZoneZero,
    HonkaiImpact3,
    TearsOfThemis
}

public static class GameEnumExtensions
{
    public static string ToFriendlyString(this Game gameName) => gameName switch
    {
        Game.Genshin => "Genshin Impact",
        Game.HonkaiStarRail => "Honkai: Star Rail",
        Game.ZenlessZoneZero => "Zenless Zone Zero",
        Game.HonkaiImpact3 => "Honkai Impact 3rd",
        Game.TearsOfThemis => "Tears of Themis",
        _ => gameName.ToString()
    };

    public static string ToGameBizString(this Game gameName) => gameName switch
    {
        Game.Genshin => "hk4e_global",
        Game.HonkaiStarRail => "hkrpg_global",
        Game.ZenlessZoneZero => "nap_global",
        _ => throw new ArgumentOutOfRangeException(nameof(gameName), gameName, null)
    };
}
