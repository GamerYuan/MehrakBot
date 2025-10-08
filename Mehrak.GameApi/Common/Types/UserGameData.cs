#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class UserGameData
{
    [JsonPropertyName("game_biz")] public string? GameBiz { get; init; }

    [JsonPropertyName("region")] public string? Region { get; init; }

    [JsonPropertyName("game_uid")] public string? GameUid { get; init; }

    [JsonPropertyName("nickname")] public string? Nickname { get; init; }

    [JsonPropertyName("level")] public int? Level { get; init; }

    [JsonPropertyName("is_chosen")] public bool? IsChosen { get; init; }

    [JsonPropertyName("region_name")] public string? RegionName { get; init; }

    [JsonPropertyName("is_official")] public bool? IsOfficial { get; init; }
}
