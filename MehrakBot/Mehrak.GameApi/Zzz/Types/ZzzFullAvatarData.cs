#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzAvatarData
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("name_mi18n")] public string Name { get; set; }

    [JsonPropertyName("full_name_mi18n")] public string FullName { get; set; }

    [JsonPropertyName("element_type")] public int ElementType { get; set; }

    [JsonPropertyName("camp_name_mi18n")] public string CampName { get; set; }

    [JsonPropertyName("avatar_profession")]
    public int AvatarProfession { get; set; }

    [JsonPropertyName("rarity")] public string Rarity { get; set; }

    [JsonPropertyName("group_icon_path")] public string GroupIconPath { get; set; }

    [JsonPropertyName("hollow_icon_path")] public string HollowIconPath { get; set; }

    [JsonPropertyName("equip")] public List<DiskDrive> Equip { get; set; }

    [JsonPropertyName("weapon")] public Weapon? Weapon { get; set; }

    [JsonPropertyName("properties")] public List<CharacterProperty> Properties { get; set; }

    [JsonPropertyName("skills")] public List<Skill> Skills { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }

    [JsonPropertyName("ranks")] public List<Rank> Ranks { get; set; }

    [JsonPropertyName("role_vertical_painting_url")]
    public string RoleVerticalPaintingUrl { get; set; }

    /// <summary>
    /// Hex color code for background color
    /// </summary>
    [JsonPropertyName("vertical_painting_color")]
    public string VerticalPaintingColor { get; set; }

    [JsonPropertyName("sub_element_type")] public int SubElementType { get; set; }

    [JsonPropertyName("role_square_url")] public string RoleSquareUrl { get; set; }

    [JsonPropertyName("awaken_state")] public string AwakenState { get; set; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.FileName, Id);
    }
}

public class ZzzFullAvatarData
{
    [JsonPropertyName("avatar_list")] public List<ZzzAvatarData> AvatarList { get; set; }

    [JsonPropertyName("equip_wiki")] public Dictionary<string, string>? EquipWiki { get; set; }

    [JsonPropertyName("weapon_wiki")] public Dictionary<string, string>? WeaponWiki { get; set; }

    [JsonPropertyName("avatar_wiki")] public Dictionary<string, string> AvatarWiki { get; set; }

    [JsonPropertyName("strategy_wiki")] public Dictionary<string, string>? StrategyWiki { get; set; }
}

public class DiskDrive
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("icon")] public string Icon { get; set; }

    [JsonPropertyName("rarity")] public string Rarity { get; set; }

    [JsonPropertyName("properties")] public List<EquipProperty> Properties { get; set; }

    [JsonPropertyName("main_properties")] public List<EquipProperty> MainProperties { get; set; }

    [JsonPropertyName("equip_suit")] public EquipSuit EquipSuit { get; set; }

    [JsonPropertyName("equipment_type")] public int EquipmentType { get; set; }

    [JsonPropertyName("invalid_property_cnt")]
    public int InvalidPropertyCnt { get; set; }

    [JsonPropertyName("all_hit")] public bool AllHit { get; set; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.FileName, EquipSuit.SuitId);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

/// <summary>
/// Disk Drive set details
/// </summary>
public sealed class EquipSuit : IEquatable<EquipSuit>
{
    [JsonPropertyName("suit_id")] public int SuitId { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("own")] public int Own { get; set; }

    [JsonPropertyName("desc1")] public string Desc1 { get; set; }

    [JsonPropertyName("desc2")] public string Desc2 { get; set; }

    public bool Equals(EquipSuit? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return SuitId == other.SuitId;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as EquipSuit);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SuitId, Name);
    }
}

public class SkillDetails
{
    [JsonPropertyName("title")] public string Title { get; set; }

    [JsonPropertyName("text")] public string Text { get; set; }

    [JsonPropertyName("awaken")] public bool Awaken { get; set; }
}

public class EquipProperty
{
    [JsonPropertyName("property_name")] public string PropertyName { get; set; }

    [JsonPropertyName("property_id")] public int PropertyId { get; set; }

    [JsonPropertyName("base")] public string Base { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("valid")] public bool Valid { get; set; }

    [JsonPropertyName("system_id")] public int SystemId { get; set; }

    [JsonPropertyName("add")] public int Add { get; set; }
}

public class CharacterProperty
{
    [JsonPropertyName("property_name")] public string PropertyName { get; set; }

    [JsonPropertyName("property_id")] public int PropertyId { get; set; }

    [JsonPropertyName("base")] public string Base { get; set; }

    [JsonPropertyName("add")] public string Add { get; set; }

    [JsonPropertyName("final")] public string Final { get; set; }
}

public class Rank
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("desc")] public string Desc { get; set; }

    [JsonPropertyName("pos")] public int Pos { get; set; }

    [JsonPropertyName("is_unlocked")] public bool IsUnlocked { get; set; }
}

public class Skill
{
    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("skill_type")] public int SkillType { get; set; }

    [JsonPropertyName("items")] public List<SkillDetails> Items { get; set; }

    [JsonPropertyName("awaken_state")] public string AwakenState { get; set; }
}

public class Weapon
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    [JsonPropertyName("star")] public int Star { get; set; }

    [JsonPropertyName("icon")] public string Icon { get; set; }

    [JsonPropertyName("rarity")] public string Rarity { get; set; }

    [JsonPropertyName("properties")] public List<EquipProperty> Properties { get; set; }

    [JsonPropertyName("main_properties")] public List<EquipProperty> MainProperties { get; set; }

    [JsonPropertyName("talent_title")] public string TalentTitle { get; set; }

    [JsonPropertyName("talent_content")] public string TalentContent { get; set; }

    [JsonPropertyName("profession")] public int Profession { get; set; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}
