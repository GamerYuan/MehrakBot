#region

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Genshin.Types;

public class CharacterListData
{
    [JsonPropertyName("list")] public required List<GenshinBasicCharacterData> List { get; init; }
}

public class GenshinBasicCharacterData
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

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Genshin.AvatarName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
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

    [NotNull]
    public bool? Ascended
    {
        get
        {
            if (field is null) return Level > 40;
            return field;
        }
        set;
    }

    public string ToBaseImageName()
    {
        return string.Format(FileNameFormat.Genshin.WeaponBaseName, Id);
    }

    public string ToAscendedImageName()
    {
        return string.Format(FileNameFormat.Genshin.WeaponAscendedName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToBaseImageName(), Icon);
    }
}
