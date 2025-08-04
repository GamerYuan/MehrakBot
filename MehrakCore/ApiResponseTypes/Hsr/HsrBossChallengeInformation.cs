#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Hsr;

public class HsrBossFloorDetail
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("star_num")] public required string StarNum { get; set; }

    [JsonPropertyName("node_1")] public required HsrBossNode Node1 { get; set; }

    [JsonPropertyName("node_2")] public required HsrBossNode Node2 { get; set; }

    [JsonPropertyName("maze_id")] public int MazeId { get; set; }

    [JsonPropertyName("is_fast")] public bool IsFast { get; set; }

    [JsonPropertyName("last_update_time")] public ScheduleTime? LastUpdateTime { get; set; }
}

public class BossAvatar
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }

    [JsonPropertyName("rarity")] public int Rarity { get; set; }

    [JsonPropertyName("element")] public required string Element { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }
}

public class BossBuff
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; set; }

    [JsonPropertyName("desc_mi18n")] public required string Desc { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }
}

public class HsrBossChallengeInformation
{
    [JsonPropertyName("groups")] public required List<HsrBossGroup> Groups { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("max_floor")] public string? MaxFloor { get; set; }

    [JsonPropertyName("battle_num")] public int BattleNum { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }

    [JsonPropertyName("all_floor_detail")] public List<HsrBossFloorDetail>? AllFloorDetail { get; set; }

    [JsonPropertyName("max_floor_id")] public int MaxFloorId { get; set; }
}

public class HsrBossGroup
{
    [JsonPropertyName("schedule_id")] public int ScheduleId { get; set; }

    [JsonPropertyName("begin_time")] public required ScheduleTime BeginTime { get; set; }

    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; set; }

    [JsonPropertyName("status")] public required string Status { get; set; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; set; }

    [JsonPropertyName("upper_boss")] public required HsrChallengeBoss UpperBoss { get; set; }

    [JsonPropertyName("lower_boss")] public required HsrChallengeBoss LowerBoss { get; set; }
}

public class HsrBossNode
{
    [JsonPropertyName("challenge_time")] public required ScheduleTime ChallengeTime { get; set; }

    [JsonPropertyName("avatars")] public required List<BossAvatar> Avatars { get; set; }

    [JsonPropertyName("buff")] public required BossBuff Buff { get; set; }

    [JsonPropertyName("score")] public required string Score { get; set; }

    [JsonPropertyName("boss_defeated")] public bool BossDefeated { get; set; }
}

public class HsrChallengeBoss
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }
}
