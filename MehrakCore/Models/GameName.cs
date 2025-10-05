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

public static class GameNameExtensions
{
    public static string ToFriendlyString(this GameName gameName) => gameName switch
    {
        GameName.Genshin => "Genshin Impact",
        GameName.HonkaiStarRail => "Honkai: Star Rail",
        GameName.ZenlessZoneZero => "Zenless Zone Zero",
        GameName.HonkaiImpact3 => "Honkai Impact 3rd",
        GameName.TearsOfThemis => "Tears of Themis",
        _ => gameName.ToString()
    };
}
