#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class CharacterDetailApiResponse
{
    [JsonPropertyName("retcode")] public int Retcode { get; set; }

    [JsonPropertyName("message")] public string Message { get; set; }

    [JsonPropertyName("data")] public GenshinCharacterDetail Data { get; set; }
}

public class GenshinCharacterDetail : ICharacterDetail

{
    [JsonPropertyName("list")] public List<GenshinCharacterInformation> List { get; set; }

    [JsonPropertyName("avatar_wiki")] public Dictionary<string, string> AvatarWiki { get; set; }
}

public class BaseCharacterDetail
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

public class Constellation
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("effect")] public string Effect { get; set; }

    [JsonPropertyName("is_actived")] public bool? IsActived { get; set; }

    [JsonPropertyName("pos")] public int? Pos { get; set; }
}

public class StatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; set; }

    [JsonPropertyName("base")] public string Base { get; set; }
    [JsonPropertyName("add")] public string Add { get; set; }

    [JsonPropertyName("final")] public string Final { get; set; }
}

public class GenshinCharacterInformation : ICharacterInformation
{
    [JsonPropertyName("base")] public BaseCharacterDetail Base { get; set; }
    [JsonPropertyName("weapon")] public WeaponDetail Weapon { get; set; }
    [JsonPropertyName("relics")] public List<Relic> Relics { get; set; }

    [JsonPropertyName("constellations")] public List<Constellation> Constellations { get; set; }

    [JsonPropertyName("costumes")] public List<object> Costumes { get; set; }

    [JsonPropertyName("selected_properties")]
    public List<StatProperty> SelectedProperties { get; set; }

    [JsonPropertyName("base_properties")] public List<StatProperty> BaseProperties { get; set; }

    [JsonPropertyName("extra_properties")] public List<StatProperty> ExtraProperties { get; set; }

    [JsonPropertyName("element_properties")]
    public List<StatProperty> ElementProperties { get; set; }

    [JsonPropertyName("skills")] public List<Skill> Skills { get; set; }
}

public class Skill
{
    [JsonPropertyName("skill_id")] public int? SkillId { get; set; }

    [JsonPropertyName("skill_type")] public int? SkillType { get; set; }

    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }
}

public class RelicStatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; set; }

    [JsonPropertyName("value")] public string Value { get; set; }

    [JsonPropertyName("times")] public int? Times { get; set; }
}

public class Relic
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("pos")] public int? Pos { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("set")] public RelicSet RelicSet { get; set; }

    [JsonPropertyName("pos_name")] public string PosName { get; set; }

    [JsonPropertyName("main_property")] public RelicStatProperty MainProperty { get; set; }

    [JsonPropertyName("sub_property_list")]
    public List<RelicStatProperty> SubPropertyList { get; set; }
}

public class RelicSet : IEquatable<RelicSet>
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("affixes")] public List<RelicAffix> Affixes { get; set; }

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
    public int? ActivationNumber { get; set; }

    [JsonPropertyName("effect")] public string Effect { get; set; }
}

public class WeaponDetail
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("type")] public int? Type { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }

    [JsonPropertyName("affix_level")] public int? AffixLevel { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("promote_level")] public int? PromoteLevel { get; set; }

    [JsonPropertyName("type_name")] public string TypeName { get; set; }

    [JsonPropertyName("desc")] public string Desc { get; set; }

    [JsonPropertyName("main_property")] public StatProperty MainProperty { get; set; }

    [JsonPropertyName("sub_property")] public StatProperty? SubProperty { get; set; }
}
