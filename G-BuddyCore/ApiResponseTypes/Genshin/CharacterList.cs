#region

using System.Text.Json.Serialization;

#endregion

namespace G_BuddyCore.ApiResponseTypes.Genshin;

public record CharacterListApiResponse(
    [property: JsonPropertyName("retcode")]
    int Retcode,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("data")] CharacterListData Data
);

public record CharacterListData(
    [property: JsonPropertyName("list")] IReadOnlyList<BasicCharacterData> List
);

public record BasicCharacterData(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("element")]
    string Element,
    [property: JsonPropertyName("fetter")] int? Fetter,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("rarity")] int? Rarity,
    [property: JsonPropertyName("actived_constellation_num")]
    int? ActivedConstellationNum,
    [property: JsonPropertyName("image")] string Image,
    [property: JsonPropertyName("is_chosen")]
    bool? IsChosen,
    [property: JsonPropertyName("side_icon")]
    string SideIcon,
    [property: JsonPropertyName("weapon_type")]
    int? WeaponType,
    [property: JsonPropertyName("weapon")] Weapon Weapon
);

public record Weapon(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("type")] int? Type,
    [property: JsonPropertyName("rarity")] int? Rarity,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("affix_level")]
    int? AffixLevel,
    [property: JsonPropertyName("name")] string Name
);
