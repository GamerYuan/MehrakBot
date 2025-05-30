#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Hsr;

public class HsrCharacterInformation : IBasicCharacterData, ICharacterInformation, ICharacterDetail
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("element")] public string Element { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
    [JsonPropertyName("rank")] public int? Rank { get; set; }
    [JsonPropertyName("image")] public string Image { get; set; }
    [JsonPropertyName("equip")] public Equip Equip { get; set; }
    [JsonPropertyName("relics")] public List<Relic> Relics { get; } = new();
    [JsonPropertyName("ornaments")] public List<Relic> Ornaments { get; } = new();
    [JsonPropertyName("ranks")] public List<Rank> Ranks { get; } = new();
    [JsonPropertyName("properties")] public List<Property> Properties { get; } = new();
    [JsonPropertyName("skills")] public List<Skill> Skills { get; } = new();
    [JsonPropertyName("base_type")] public int? BaseType { get; set; }
    [JsonPropertyName("figure_path")] public string FigurePath { get; set; }
    [JsonPropertyName("element_id")] public int? ElementId { get; set; }
    [JsonPropertyName("servant_detail")] public ServantDetail ServantDetail { get; set; }
}

public class Data
{
    [JsonPropertyName("avatar_list")] public List<HsrCharacterInformation> AvatarList { get; set; }
    [JsonPropertyName("equip_wiki")] public Dictionary<string, string> EquipWiki { get; set; }
    [JsonPropertyName("relic_wiki")] public Dictionary<string, string> RelicWiki { get; set; }

    [JsonPropertyName("relic_properties")] public List<RelicProperty> RelicProperties { get; set; }
}

public class Equip
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("rank")] public int? Rank { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
}

public class ExclusiveSkill
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; }
}

public class MainProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; set; }
    [JsonPropertyName("value")] public string Value { get; set; }
    [JsonPropertyName("times")] public int? Times { get; set; }
}

public class Property
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; set; }
    [JsonPropertyName("value")] public string Value { get; set; }
    [JsonPropertyName("times")] public int? Times { get; set; }
    [JsonPropertyName("base")] public string Base { get; set; }
    [JsonPropertyName("add")] public string Add { get; set; }
    [JsonPropertyName("final")] public string Final { get; set; }
}

public class Rank
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("pos")] public int? Pos { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; }
    [JsonPropertyName("is_unlocked")] public bool? IsUnlocked { get; set; }
}

public class Relic
{
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("pos")] public int? Pos { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("desc")] public string Desc { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; }
    [JsonPropertyName("rarity")] public int? Rarity { get; set; }
    [JsonPropertyName("main_property")] public MainProperty MainProperty { get; set; }
    [JsonPropertyName("properties")] public List<Property> Properties { get; set; }
}

public class RelicProperty
{
    [JsonPropertyName("property_type")] public int? PropertyType { get; set; }

    [JsonPropertyName("modify_property_type")]
    public int? ModifyPropertyType { get; set; }
}

public class CharacterListApiResponse
{
    [JsonPropertyName("retcode")] public int? Retcode { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("data")] public Data Data { get; set; }
}

public class ServantDetail
{
    [JsonPropertyName("servant_id")] public string ServantId { get; set; }
    [JsonPropertyName("servant_name")] public string ServantName { get; set; }
    [JsonPropertyName("servant_icon")] public string ServantIcon { get; set; }

    [JsonPropertyName("servant_properties")]
    public List<object> ServantProperties { get; } = new();

    [JsonPropertyName("servant_skills")] public List<object> ServantSkills { get; set; }
    [JsonPropertyName("is_health_secret")] public bool? IsHealthSecret { get; set; }
}

public class Skill
{
    [JsonPropertyName("point_id")] public string PointId { get; set; }
    [JsonPropertyName("point_type")] public int? PointType { get; set; }
    [JsonPropertyName("item_url")] public string ItemUrl { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("is_activated")] public bool? IsActivated { get; set; }
    [JsonPropertyName("is_rank_work")] public bool? IsRankWork { get; set; }
    [JsonPropertyName("pre_point")] public string PrePoint { get; set; }
    [JsonPropertyName("anchor")] public string Anchor { get; set; }
    [JsonPropertyName("remake")] public string Remake { get; set; }
    [JsonPropertyName("skill_stages")] public List<SkillStage> SkillStages { get; } = new();

    [JsonPropertyName("special_point_type")]
    public string SpecialPointType { get; set; }
}

public class SkillStage
{
    [JsonPropertyName("desc")] public string Desc { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("remake")] public string Remake { get; set; }
    [JsonPropertyName("item_url")] public string ItemUrl { get; set; }
    [JsonPropertyName("is_activated")] public bool? IsActivated { get; set; }
    [JsonPropertyName("is_rank_work")] public bool? IsRankWork { get; set; }
    [JsonPropertyName("exclusive_skill")] public ExclusiveSkill ExclusiveSkill { get; set; }

    [JsonPropertyName("special_point_type")]
    public string SpecialPointType { get; set; }
}
