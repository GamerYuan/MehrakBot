#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Genshin;

public class Avatar
{
    [JsonPropertyName("id")] public int? Id { get; init; }

    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("level")] public int? Level { get; init; }

    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
}

public class Battle
{
    [JsonPropertyName("index")] public int? Index { get; init; }

    [JsonPropertyName("timestamp")] public string? Timestamp { get; init; }

    [JsonPropertyName("avatars")] public List<Avatar>? Avatars { get; init; }

    [JsonPropertyName("settle_date_time")] public SettleDateTime? SettleDateTime { get; init; }
}

public class GenshinAbyssInformation
{
    [JsonPropertyName("schedule_id")] public int? ScheduleId { get; init; }

    [JsonPropertyName("start_time")] public string? StartTime { get; init; }

    [JsonPropertyName("end_time")] public string? EndTime { get; init; }

    [JsonPropertyName("total_battle_times")]
    public int? TotalBattleTimes { get; init; }

    [JsonPropertyName("total_win_times")] public int? TotalWinTimes { get; init; }

    [JsonPropertyName("max_floor")] public string? MaxFloor { get; init; }

    [JsonPropertyName("reveal_rank")] public List<RankDetails>? RevealRank { get; init; }

    [JsonPropertyName("defeat_rank")] public List<RankDetails>? DefeatRank { get; init; }

    [JsonPropertyName("damage_rank")] public List<RankDetails>? DamageRank { get; init; }

    [JsonPropertyName("take_damage_rank")] public List<RankDetails>? TakeDamageRank { get; init; }

    [JsonPropertyName("normal_skill_rank")]
    public List<RankDetails>? NormalSkillRank { get; init; }

    [JsonPropertyName("energy_skill_rank")]
    public List<RankDetails>? EnergySkillRank { get; init; }

    [JsonPropertyName("floors")] public List<Floor>? Floors { get; init; }

    [JsonPropertyName("total_star")] public int? TotalStar { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("is_just_skipped_floor")]
    public bool? IsJustSkippedFloor { get; init; }

    [JsonPropertyName("skipped_floor")] public string? SkippedFloor { get; init; }
}

public class Floor
{
    [JsonPropertyName("index")] public int? Index { get; init; }

    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("settle_time")] public string? SettleTime { get; init; }

    [JsonPropertyName("star")] public int? Star { get; init; }

    [JsonPropertyName("max_star")] public int? MaxStar { get; init; }

    [JsonPropertyName("levels")] public List<Level>? Levels { get; init; }

    [JsonPropertyName("settle_date_time")] public SettleDateTime? SettleDateTime { get; init; }

    [JsonPropertyName("ley_line_disorder")]
    public List<string>? LeyLineDisorder { get; init; }
}

public class Level
{
    [JsonPropertyName("index")] public int? Index { get; init; }

    [JsonPropertyName("star")] public int? Star { get; init; }

    [JsonPropertyName("max_star")] public int? MaxStar { get; init; }

    [JsonPropertyName("battles")] public List<Battle>? Battles { get; init; }

    [JsonPropertyName("top_half_floor_monster")]
    public List<MonsterList>? TopHalfFloorMonster { get; init; }

    [JsonPropertyName("bottom_half_floor_monster")]
    public List<MonsterList>? BottomHalfFloorMonster { get; init; }
}

public class RankDetails
{
    [JsonPropertyName("avatar_id")] public int? AvatarId { get; init; }

    [JsonPropertyName("avatar_icon")] public string? AvatarIcon { get; init; }

    [JsonPropertyName("value")] public int? Value { get; init; }

    [JsonPropertyName("rarity")] public int? Rarity { get; init; }
}

public class SettleDateTime
{
    [JsonPropertyName("year")] public int? Year { get; init; }

    [JsonPropertyName("month")] public int? Month { get; init; }

    [JsonPropertyName("day")] public int? Day { get; init; }

    [JsonPropertyName("hour")] public int? Hour { get; init; }

    [JsonPropertyName("minute")] public int? Minute { get; init; }

    [JsonPropertyName("second")] public int? Second { get; init; }
}

public class MonsterList
{
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("level")] public int? Level { get; init; }
}
