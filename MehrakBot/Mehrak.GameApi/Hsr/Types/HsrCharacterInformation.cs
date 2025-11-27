#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Utility;

#endregion

namespace Mehrak.GameApi.Hsr.Types;

public class HsrCharacterInformation
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("element")] public required string Element { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("image")] public required string Image { get; init; }
    [JsonPropertyName("equip")] public Equip? Equip { get; init; }
    [JsonPropertyName("relics")] public required List<Relic> Relics { get; init; }
    [JsonPropertyName("ornaments")] public required List<Relic> Ornaments { get; init; }
    [JsonPropertyName("ranks")] public required List<Rank> Ranks { get; init; }
    [JsonPropertyName("properties")] public required List<Property> Properties { get; init; }
    [JsonPropertyName("skills")] public required List<Skill> Skills { get; init; }
    [JsonPropertyName("base_type")] public int? BaseType { get; init; }
    [JsonPropertyName("figure_path")] public string? FigurePath { get; init; }
    [JsonPropertyName("element_id")] public int? ElementId { get; init; }
    [JsonPropertyName("servant_detail")] public ServantDetail? ServantDetail { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Image);
    }

    public string ToAvatarImageName()
    {
        return string.Format(FileNameFormat.Hsr.AvatarName, Id);
    }

    public IImageData ToAvatarImageData()
    {
        return new ImageData(ToAvatarImageName(), Icon);
    }
}

public class HsrBasicCharacterData
{
    [JsonPropertyName("avatar_list")] public required List<HsrCharacterInformation> AvatarList { get; init; }
    [JsonPropertyName("equip_wiki")] public required Dictionary<string, string> EquipWiki { get; init; }
    [JsonPropertyName("relic_wiki")] public required Dictionary<string, string> RelicWiki { get; init; }
}

public class Equip
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("desc")] public string? Desc { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.FileName, Id);
    }

    public string ToIconImageName()
    {
        return string.Format(FileNameFormat.Hsr.WeaponIconName, Id);
    }

    public IImageData ToIconImageData()
    {
        return new ImageData(ToIconImageName(), Icon);
    }
}

public class ExclusiveSkill
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("desc")] public string? Desc { get; init; }
}

public class MainProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("times")] public int? Times { get; init; }
}

public class Property
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("times")] public int? Times { get; init; }
    [JsonPropertyName("base")] public string? Base { get; init; }
    [JsonPropertyName("add")] public string? Add { get; init; }
    [JsonPropertyName("final")] public string? Final { get; init; }
}

public class Rank
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("pos")] public int Pos { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("desc")] public string? Desc { get; init; }
    [JsonPropertyName("is_unlocked")] public bool IsUnlocked { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.FileName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class Relic
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("pos")] public int Pos { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("desc")] public string? Desc { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("rarity")] public int Rarity { get; init; }
    [JsonPropertyName("main_property")] public required MainProperty MainProperty { get; init; }
    [JsonPropertyName("properties")] public required List<Property> Properties { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.FileName, Id.ToString()[1..]);
    }

    public int GetSetId()
    {
        return int.Parse(Id.ToString()[1..^1] ?? "0");
    }
}

public class ServantDetail
{
    [JsonPropertyName("servant_id")] public string? ServantId { get; init; }
    [JsonPropertyName("servant_name")] public string? ServantName { get; init; }
    [JsonPropertyName("servant_icon")] public string? ServantIcon { get; init; }

    [JsonPropertyName("servant_properties")]
    public List<Property>? ServantProperties { get; init; }

    [JsonPropertyName("servant_skills")] public List<Skill>? ServantSkills { get; init; }
    [JsonPropertyName("is_health_secret")] public bool? IsHealthSecret { get; init; }
}

public class Skill
{
    [JsonPropertyName("point_id")] public required string PointId { get; init; }
    [JsonPropertyName("point_type")] public int PointType { get; init; }
    [JsonPropertyName("item_url")] public required string ItemUrl { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("is_activated")] public bool IsActivated { get; init; }
    [JsonPropertyName("is_rank_work")] public bool IsRankWork { get; init; }
    [JsonPropertyName("pre_point")] public string? PrePoint { get; init; }
    [JsonPropertyName("anchor")] public string? Anchor { get; init; }
    [JsonPropertyName("remake")] public string? Remake { get; init; }
    [JsonPropertyName("skill_stages")] public List<SkillStage>? SkillStages { get; init; }

    [JsonPropertyName("special_point_type")]
    public string? SpecialPointType { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.FileName,
            PointType == 1 ? RegexExpressions.HsrStatBonusRegex().Replace(SkillStages![0].Name!, "") : PointId);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), ItemUrl);
    }
}

public class SkillStage
{
    [JsonPropertyName("desc")] public string? Desc { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("level")] public int? Level { get; init; }
    [JsonPropertyName("remake")] public string? Remake { get; init; }
    [JsonPropertyName("item_url")] public string? ItemUrl { get; init; }
    [JsonPropertyName("is_activated")] public bool? IsActivated { get; init; }
    [JsonPropertyName("is_rank_work")] public bool? IsRankWork { get; init; }
    [JsonPropertyName("exclusive_skill")] public ExclusiveSkill? ExclusiveSkill { get; init; }

    [JsonPropertyName("special_point_type")]
    public string? SpecialPointType { get; init; }
}
