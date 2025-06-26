#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class CharacterDetailApiResponse
{
    [JsonPropertyName("retcode")] public int Retcode { get; init; }

    [JsonPropertyName("message")] public string? Message { get; init; }

    [JsonPropertyName("data")] public GenshinCharacterDetail? Data { get; init; }
}

public class GenshinCharacterDetail : ICharacterDetail

{
    [JsonPropertyName("list")] public required List<GenshinCharacterInformation> List { get; init; }

    [JsonPropertyName("avatar_wiki")] public required Dictionary<string, string> AvatarWiki { get; init; }
}

public class BaseCharacterDetail
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

public class Constellation
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("effect")] public string? Effect { get; init; }

    [JsonPropertyName("is_actived")] public bool? IsActived { get; init; }

    [JsonPropertyName("pos")] public int? Pos { get; init; }
}

public class StatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }

    [JsonPropertyName("base")] public required string Base { get; init; }
    [JsonPropertyName("add")] public string? Add { get; init; }

    [JsonPropertyName("final")] public required string Final { get; init; }
}

public class GenshinCharacterInformation : ICharacterInformation
{
    [JsonPropertyName("base")] public required BaseCharacterDetail Base { get; init; }
    [JsonPropertyName("weapon")] public required WeaponDetail Weapon { get; init; }
    [JsonPropertyName("relics")] public required IReadOnlyList<Relic> Relics { get; init; }

    [JsonPropertyName("constellations")] public required List<Constellation> Constellations { get; init; }

    [JsonPropertyName("costumes")] public List<object>? Costumes { get; init; }

    [JsonPropertyName("selected_properties")]
    public required IReadOnlyList<StatProperty> SelectedProperties { get; init; }

    [JsonPropertyName("base_properties")] public required List<StatProperty> BaseProperties { get; init; }

    [JsonPropertyName("extra_properties")] public required List<StatProperty> ExtraProperties { get; init; }

    [JsonPropertyName("element_properties")]
    public required List<StatProperty> ElementProperties { get; init; }

    [JsonPropertyName("skills")] public required List<Skill> Skills { get; init; }

    public override string ToString()
    {
        return
            $"Id: {Base.Id}, Name: {Base.Name}, Weapon: {Weapon.Name}, Artifacts: {string.Join(", ", Relics.Select(x => x.Name))}";
    }
}

public class Skill
{
    [JsonPropertyName("skill_id")] public int? SkillId { get; init; }

    [JsonPropertyName("skill_type")] public int? SkillType { get; init; }

    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("desc")] public required string Desc { get; init; }
    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}

public class RelicStatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }

    [JsonPropertyName("value")] public required string Value { get; init; }

    [JsonPropertyName("times")] public int? Times { get; init; }
}

public class Relic
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("pos")] public int? Pos { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("set")] public required RelicSet RelicSet { get; init; }

    [JsonPropertyName("pos_name")] public required string PosName { get; init; }

    [JsonPropertyName("main_property")] public required RelicStatProperty MainProperty { get; init; }

    [JsonPropertyName("sub_property_list")]
    public required List<RelicStatProperty> SubPropertyList { get; init; }
}

public class RelicSet : IEquatable<RelicSet>
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("affixes")] public required List<RelicAffix> Affixes { get; init; }

    public override bool Equals(object? other)
    {
        return Equals(other as RelicSet);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
    }

    public bool Equals(RelicSet? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id && Name == other.Name;
    }
}

public class RelicAffix
{
    [JsonPropertyName("activation_number")]
    public int? ActivationNumber { get; init; }

    [JsonPropertyName("effect")] public string? Effect { get; init; }
}

public class WeaponDetail
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("type")] public int? Type { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }

    [JsonPropertyName("affix_level")] public int? AffixLevel { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("promote_level")] public int? PromoteLevel { get; init; }

    [JsonPropertyName("type_name")] public required string TypeName { get; init; }

    [JsonPropertyName("desc")] public string? Desc { get; init; }

    [JsonPropertyName("main_property")] public required StatProperty MainProperty { get; init; }

    [JsonPropertyName("sub_property")] public StatProperty? SubProperty { get; init; }
}
