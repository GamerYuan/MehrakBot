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

    // ponytail: reverse mapping from API region string to Server enum name.
    // returns the Server/Hi3Server enum name for DB storage.
    public static string RegionToServerString(this Game game, string region)
    {
        return game switch
        {
            Game.Genshin => region switch
            {
                "os_asia" => Server.Asia.ToString(),
                "os_euro" => Server.Europe.ToString(),
                "os_usa" => Server.America.ToString(),
                "os_cht" => Server.Sar.ToString(),
                _ => region
            },
            Game.HonkaiStarRail => region switch
            {
                "prod_official_asia" => Server.Asia.ToString(),
                "prod_official_eur" => Server.Europe.ToString(),
                "prod_official_usa" => Server.America.ToString(),
                "prod_official_cht" => Server.Sar.ToString(),
                _ => region
            },
            Game.ZenlessZoneZero => region switch
            {
                "prod_gf_jp" => Server.Asia.ToString(),
                "prod_gf_eu" => Server.Europe.ToString(),
                "prod_gf_us" => Server.America.ToString(),
                "prod_gf_sg" => Server.Sar.ToString(),
                _ => region
            },
            Game.HonkaiImpact3 => region switch
            {
                "overseas01" => Hi3Server.SEA.ToString(),
                "jp01" => Hi3Server.JP.ToString(),
                "kr01" => Hi3Server.KR.ToString(),
                "usa01" => Hi3Server.America.ToString(),
                "asia01" => Hi3Server.SAR.ToString(),
                "eur01" => Hi3Server.Europe.ToString(),
                _ => region
            },
            _ => region
        };
    }
}
