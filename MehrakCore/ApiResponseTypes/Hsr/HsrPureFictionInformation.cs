#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Hsr;

public class FictionFloorDetail
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("round_num")] public int RoundNum { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("node_1")] public FictionNodeInformation? Node1 { get; set; }

    [JsonPropertyName("node_2")] public FictionNodeInformation? Node2 { get; set; }

    [JsonPropertyName("maze_id")] public int MazeId { get; set; }

    [JsonPropertyName("is_fast")] public bool IsFast { get; set; }
}

public class FictionAvatar
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("level")] public int Level { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }

    [JsonPropertyName("rarity")] public int Rarity { get; set; }

    [JsonPropertyName("element")] public required string Element { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }
}

public class FictionBuff
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name_mi18n")] public required string NameMi18N { get; set; }

    [JsonPropertyName("desc_mi18n")] public required string DescMi18N { get; set; }

    [JsonPropertyName("icon")] public required string Icon { get; set; }
}

public class Group
{
    [JsonPropertyName("schedule_id")] public int ScheduleId { get; set; }

    [JsonPropertyName("begin_time")] public required ScheduleTime BeginTime { get; set; }

    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("name_mi18n")] public required string Name { get; set; }
}

public class FictionNodeInformation
{
    [JsonPropertyName("challenge_time")] public required ScheduleTime ChallengeTime { get; set; }

    [JsonPropertyName("avatars")] public required List<FictionAvatar> Avatars { get; set; }

    [JsonPropertyName("buff")] public required FictionBuff Buff { get; set; }

    [JsonPropertyName("score")] public required string Score { get; set; }
}

public class HsrPureFictionInformation
{
    [JsonPropertyName("groups")] public required List<Group> Groups { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("max_floor")] public required string MaxFloor { get; set; }

    [JsonPropertyName("battle_num")] public int BattleNum { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }

    [JsonPropertyName("all_floor_detail")] public required List<FictionFloorDetail> AllFloorDetail { get; set; }

    [JsonPropertyName("max_floor_id")] public int MaxFloorId { get; set; }
}
