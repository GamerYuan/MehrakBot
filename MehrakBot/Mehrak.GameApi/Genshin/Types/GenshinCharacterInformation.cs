#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Genshin.Types;

public class GenshinCharacterDetail

{
    [JsonPropertyName("list")] public List<GenshinCharacterInformation> List { get; init; }

    [JsonPropertyName("avatar_wiki")] public Dictionary<string, string> AvatarWiki { get; init; }
}

public class BaseCharacterDetail
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; }

    [JsonPropertyName("element")] public string? Element { get; init; }

    [JsonPropertyName("fetter")] public int? Fetter { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    [JsonPropertyName("actived_constellation_num")]
    public int? ActivedConstellationNum { get; init; }

    [JsonPropertyName("image")] public string Image { get; init; }

    [JsonPropertyName("is_chosen")] public bool IsChosen { get; init; }

    [JsonPropertyName("side_icon")] public string? SideIcon { get; init; }

    [JsonPropertyName("weapon_type")] public int? WeaponType { get; init; }

    [JsonPropertyName("weapon")] public Weapon Weapon { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Genshin.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class Constellation
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; }
    [JsonPropertyName("effect")] public string? Effect { get; init; }

    [JsonPropertyName("is_actived")] public bool? IsActived { get; init; }

    [JsonPropertyName("pos")] public int? Pos { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Genshin.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class StatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }

    [JsonPropertyName("base")] public string Base { get; init; }
    [JsonPropertyName("add")] public string? Add { get; init; }

    [JsonPropertyName("final")] public string Final { get; init; }
}

public class GenshinCharacterInformation
{
    [JsonPropertyName("base")] public BaseCharacterDetail Base { get; init; }
    [JsonPropertyName("weapon")] public WeaponDetail Weapon { get; init; }
    [JsonPropertyName("relics")] public IReadOnlyList<Relic> Relics { get; init; }

    [JsonPropertyName("constellations")] public List<Constellation> Constellations { get; init; }

    [JsonPropertyName("costumes")] public List<object>? Costumes { get; init; }

    [JsonPropertyName("selected_properties")]
    public IReadOnlyList<StatProperty> SelectedProperties { get; init; }

    [JsonPropertyName("base_properties")] public List<StatProperty> BaseProperties { get; init; }

    [JsonPropertyName("extra_properties")] public List<StatProperty> ExtraProperties { get; init; }

    [JsonPropertyName("element_properties")]
    public List<StatProperty> ElementProperties { get; init; }

    [JsonPropertyName("skills")] public List<Skill> Skills { get; init; }
}

public class Skill
{
    [JsonPropertyName("skill_id")] public int SkillId { get; init; }

    [JsonPropertyName("skill_type")] public int? SkillType { get; init; }

    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("desc")] public string Desc { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("name")] public string Name { get; init; }

    public string ToImageName(int avatarId)
    {
        return string.Format(FileNameFormat.Genshin.SkillName, avatarId, SkillId);
    }

    public IImageData ToImageData(int avatarId)
    {
        return new ImageData(ToImageName(avatarId), Icon);
    }
}

public class RelicStatProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }

    [JsonPropertyName("value")] public string Value { get; init; }

    [JsonPropertyName("times")] public int? Times { get; init; }
}

public class Relic
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; }
    [JsonPropertyName("pos")] public int? Pos { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("set")] public RelicSet RelicSet { get; init; }

    [JsonPropertyName("pos_name")] public string PosName { get; init; }

    [JsonPropertyName("main_property")] public RelicStatProperty MainProperty { get; init; }

    [JsonPropertyName("sub_property_list")]
    public List<RelicStatProperty> SubPropertyList { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Genshin.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public sealed class RelicSet : IEquatable<RelicSet>
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; }

    [JsonPropertyName("affixes")] public List<RelicAffix> Affixes { get; init; }

    public override bool Equals(object? obj)
    {
        return Equals(obj as RelicSet);
    }

    public bool Equals(RelicSet? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id && Name == other.Name;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name);
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
    [JsonPropertyName("icon")] public string Icon { get; init; }
    [JsonPropertyName("type")] public int? Type { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }

    [JsonPropertyName("affix_level")] public int? AffixLevel { get; init; }

    [JsonPropertyName("name")] public string Name { get; init; }

    [JsonPropertyName("promote_level")] public int? PromoteLevel { get; init; }

    [JsonPropertyName("type_name")] public string TypeName { get; init; }

    [JsonPropertyName("desc")] public string? Desc { get; init; }

    [JsonPropertyName("main_property")] public StatProperty MainProperty { get; init; }

    [JsonPropertyName("sub_property")] public StatProperty? SubProperty { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Genshin.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}
