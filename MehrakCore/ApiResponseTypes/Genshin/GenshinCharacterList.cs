#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class CharacterListApiResponse
{
    [JsonPropertyName("retcode")] public required int Retcode { get; init; }

    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("data")] public required CharacterListData Data { get; init; }
}

public class CharacterListData
{
    [JsonPropertyName("list")] public List<GenshinBasicCharacterData>? List { get; init; }
}

public class GenshinBasicCharacterData : IBasicCharacterData
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("element")] public string? Element { get; init; }

    [JsonPropertyName("fetter")] public int? Fetter { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }

    [JsonPropertyName("actived_constellation_num")]
    public int? ActivedConstellationNum { get; init; }

    [JsonPropertyName("image")] public string? Image { get; init; }

    [JsonPropertyName("is_chosen")] public bool? IsChosen { get; init; }

    [JsonPropertyName("side_icon")] public string? SideIcon { get; init; }

    [JsonPropertyName("weapon_type")] public int? WeaponType { get; init; }

    [JsonPropertyName("weapon")] public required Weapon Weapon { get; init; }
}

public class Weapon
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("type")] public int? Type { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }

    [JsonPropertyName("affix_level")] public int? AffixLevel { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}
