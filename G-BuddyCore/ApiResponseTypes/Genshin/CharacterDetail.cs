#region

using System.Text.Json.Serialization;

#endregion

namespace G_BuddyCore.ApiResponseTypes.Genshin;

public record CharacterDetail(
    [property: JsonPropertyName("list")] IReadOnlyList<CharacterInformation> List
);

public record BaseCharacterDetail(
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
    [property: JsonPropertyName("weapon")] WeaponDetail Weapon
);

public record Constellation(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("effect")] string Effect,
    [property: JsonPropertyName("is_actived")]
    bool? IsActived,
    [property: JsonPropertyName("pos")] int? Pos
);

public record StatProperty(
    [property: JsonPropertyName("property_type")]
    int? PropertyType,
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("add")] string Add,
    [property: JsonPropertyName("final")] string Final
);

public record CharacterInformation(
    [property: JsonPropertyName("base")] BaseCharacterDetail Base,
    [property: JsonPropertyName("weapon")] Weapon Weapon,
    [property: JsonPropertyName("relics")] IReadOnlyList<Relic> Relics,
    [property: JsonPropertyName("constellations")]
    IReadOnlyList<Constellation> Constellations,
    [property: JsonPropertyName("costumes")]
    IReadOnlyList<object> Costumes,
    [property: JsonPropertyName("selected_properties")]
    IReadOnlyList<StatProperty> SelectedProperties,
    [property: JsonPropertyName("base_properties")]
    IReadOnlyList<StatProperty> BaseProperties,
    [property: JsonPropertyName("extra_properties")]
    IReadOnlyList<StatProperty> ExtraProperties,
    [property: JsonPropertyName("element_properties")]
    IReadOnlyList<StatProperty> ElementProperties,
    [property: JsonPropertyName("skills")] IReadOnlyList<Skill> Skills
);

public record Skill(
    [property: JsonPropertyName("skill_id")]
    int? SkillId,
    [property: JsonPropertyName("skill_type")]
    int? SkillType,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("desc")] string Desc,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("is_unlock")]
    bool? IsUnlock,
    [property: JsonPropertyName("name")] string Name
);

public record RelicStatProperty(
    [property: JsonPropertyName("property_type")]
    int? PropertyType,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("times")] int? Times
);

public record Relic(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("pos")] int? Pos,
    [property: JsonPropertyName("rarity")] int? Rarity,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("set")] RelicSet RelicSet,
    [property: JsonPropertyName("pos_name")]
    string PosName,
    [property: JsonPropertyName("main_property")]
    RelicStatProperty MainProperty,
    [property: JsonPropertyName("sub_property_list")]
    IReadOnlyList<RelicStatProperty> SubPropertyList
);

public record RelicSet(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("affixes")]
    IReadOnlyList<RelicAffix> Affixes
);

public record RelicAffix(
    [property: JsonPropertyName("activation_number")]
    int? ActivationNumber,
    [property: JsonPropertyName("effect")] string Effect
);

public record WeaponDetail(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("type")] int? Type,
    [property: JsonPropertyName("rarity")] int? Rarity,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("affix_level")]
    int? AffixLevel,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("promote_level")]
    int? PromoteLevel,
    [property: JsonPropertyName("type_name")]
    string TypeName,
    [property: JsonPropertyName("desc")] string Desc,
    [property: JsonPropertyName("main_property")]
    StatProperty RelicStatProperty,
    [property: JsonPropertyName("sub_property")]
    StatProperty SubProperty
);
