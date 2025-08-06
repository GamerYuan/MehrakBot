#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Hsr;

public class HsrEndFloorDetail
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("round_num")] public int RoundNum { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("node_1")] public HsrEndNodeInformation? Node1 { get; set; }

    [JsonPropertyName("node_2")] public HsrEndNodeInformation? Node2 { get; set; }

    [JsonPropertyName("maze_id")] public int MazeId { get; set; }

    [JsonPropertyName("is_fast")] public bool IsFast { get; set; }
}

public class HsrEndAvatar
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }

    [JsonPropertyName("rarity")] public int Rarity { get; set; }

    [JsonPropertyName("element")] public required string Element { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }
}

public class HsrEndBuff
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name_mi18n")] public required string NameMi18N { get; set; }

    [JsonPropertyName("desc_mi18n")] public required string DescMi18N { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }
}

public class HsrEndGroup
{
    [JsonPropertyName("schedule_id")] public int ScheduleId { get; set; }

    [JsonPropertyName("begin_time")] public required ScheduleTime BeginTime { get; set; }

    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; set; }
}

public class HsrEndNodeInformation
{
    [JsonPropertyName("challenge_time")] public required ScheduleTime ChallengeTime { get; set; }

    [JsonPropertyName("avatars")] public required List<HsrEndAvatar> Avatars { get; set; }

    [JsonPropertyName("buff")] public required HsrEndBuff Buff { get; set; }

    [JsonPropertyName("score")] public required string Score { get; set; }

    /// <summary>
    /// Only used for Apocalyptic Shadow
    /// </summary>
    [JsonPropertyName("boss_defeated")]
    public bool BossDefeated { get; set; }
}

public class HsrEndInformation
{
    [JsonPropertyName("groups")] public required List<HsrEndGroup> Groups { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("max_floor")] public required string MaxFloor { get; set; }

    [JsonPropertyName("battle_num")] public int BattleNum { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }

    [JsonPropertyName("all_floor_detail")] public required List<HsrEndFloorDetail> AllFloorDetail { get; set; }

    [JsonPropertyName("max_floor_id")] public int MaxFloorId { get; set; }
}

public class ScheduleTime
{
    [JsonPropertyName("year")] public int Year { get; set; }

    [JsonPropertyName("month")] public int Month { get; set; }

    [JsonPropertyName("day")] public int Day { get; set; }

    [JsonPropertyName("hour")] public int Hour { get; set; }

    [JsonPropertyName("minute")] public int Minute { get; set; }

    public DateTime ToDateTime()
    {
        return new DateTime(Year, Month, Day, Hour, Minute, 0);
    }
}
