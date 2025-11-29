#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class UserData
{
    [JsonPropertyName("list")] public List<GameData> List { get; set; }
}

public class GameDataEntry
{
    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("type")] public int Type { get; set; }

    [JsonPropertyName("value")] public string Value { get; set; }
}

public class GameData
{
    [JsonPropertyName("has_role")] public bool? HasRole { get; set; }

    [JsonPropertyName("game_id")] public int? GameId { get; set; }

    [JsonPropertyName("game_role_id")] public string GameRoleId { get; set; }

    [JsonPropertyName("nickname")] public string Nickname { get; set; }

    [JsonPropertyName("region")] public string Region { get; set; }

    [JsonPropertyName("level")] public int? Level { get; set; }

    [JsonPropertyName("data")] public List<GameDataEntry> Data { get; set; }

    [JsonPropertyName("region_name")] public string RegionName { get; set; }

    [JsonPropertyName("game_name")] public string GameName { get; set; }
}
