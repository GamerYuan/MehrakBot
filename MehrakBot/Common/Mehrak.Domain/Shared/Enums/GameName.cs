#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.Domain.Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Game
{
    Unsupported,
    Genshin,
    HonkaiStarRail,
    ZenlessZoneZero,
    HonkaiImpact3,
    TearsOfThemis
}

public static class GameEnumExtensions
{
    public static string ToFriendlyString(this Game gameName)
    {
        return gameName switch
        {
            Game.Genshin => "Genshin Impact",
            Game.HonkaiStarRail => "Honkai: Star Rail",
            Game.ZenlessZoneZero => "Zenless Zone Zero",
            Game.HonkaiImpact3 => "Honkai Impact 3rd",
            Game.TearsOfThemis => "Tears of Themis",
            _ => gameName.ToString()
        };
    }

    public static string ToGameBizString(this Game gameName)
    {
        return gameName switch
        {
            Game.Genshin => "hk4e_global",
            Game.HonkaiStarRail => "hkrpg_global",
            Game.ZenlessZoneZero => "nap_global",
            Game.HonkaiImpact3 => "bh3_global",
            _ => throw new ArgumentOutOfRangeException(nameof(gameName), gameName, null)
        };
    }

    public static Game FromGameBizString(string gameBiz)
    {
        return gameBiz switch
        {
            "hk4e_global" => Game.Genshin,
            "hkrpg_global" => Game.HonkaiStarRail,
            "nap_global" => Game.ZenlessZoneZero,
            "bh3_global" => Game.HonkaiImpact3,
            _ => Game.Unsupported
        };
    }
}
