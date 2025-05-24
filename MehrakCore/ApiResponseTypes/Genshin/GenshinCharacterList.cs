#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class CharacterListApiResponse
{
    [JsonPropertyName("retcode")] public int Retcode { get; set; }

    [JsonPropertyName("message")] public string Message { get; set; }

    [JsonPropertyName("data")] public CharacterListData Data { get; set; }
}

public class CharacterListData
{
    [JsonPropertyName("list")] public List<GenshinBasicCharacterData> List { get; set; }
}

public class GenshinBasicCharacterData : IBasicCharacterData
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("element")] public string Element { get; set; }

    [JsonPropertyName("fetter")] public int? Fetter { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }

    [JsonPropertyName("actived_constellation_num")]
    public int? ActivedConstellationNum { get; set; }

    [JsonPropertyName("image")] public string Image { get; set; }

    [JsonPropertyName("is_chosen")] public bool? IsChosen { get; set; }

    [JsonPropertyName("side_icon")] public string SideIcon { get; set; }

    [JsonPropertyName("weapon_type")] public int? WeaponType { get; set; }

    [JsonPropertyName("weapon")] public Weapon Weapon { get; set; }
}

public class Weapon
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("type")] public int? Type { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }

    [JsonPropertyName("affix_level")] public int? AffixLevel { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }
}
