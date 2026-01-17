#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class UserData
{
    [JsonPropertyName("list")] public required List<GameData> List { get; set; }
}

public class GameDataEntry
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("type")] public int Type { get; set; }

    [JsonPropertyName("value")] public required string Value { get; set; }
}

public class GameData
{
    [JsonPropertyName("has_role")] public bool? HasRole { get; set; }

    [JsonPropertyName("game_id")] public int? GameId { get; set; }

    [JsonPropertyName("game_role_id")] public required string GameRoleId { get; set; }

    [JsonPropertyName("nickname")] public required string Nickname { get; set; }

    [JsonPropertyName("region")] public required string Region { get; set; }

    [JsonPropertyName("level")] public int? Level { get; set; }

    [JsonPropertyName("data")] public required List<GameDataEntry> Data { get; set; }

    [JsonPropertyName("region_name")] public required string RegionName { get; set; }

    [JsonPropertyName("game_name")] public required string GameName { get; set; }
}