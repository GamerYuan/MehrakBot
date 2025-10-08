#region

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Hsr.Types;

public class FloorDetail
{
    [JsonPropertyName("name")] public required string Name { get; set; }

    [JsonPropertyName("round_num")] public int RoundNum { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("node_1")] public required NodeInformation Node1 { get; set; }

    [JsonPropertyName("node_2")] public required NodeInformation Node2 { get; set; }

    [JsonPropertyName("is_chaos")] public bool IsChaos { get; set; }

    [JsonPropertyName("maze_id")] public int MazeId { get; set; }

    [JsonPropertyName("is_fast")] public bool IsFast { get; set; }
}

public class HsrMemoryInformation
{
    [JsonPropertyName("schedule_id")] public int ScheduleId { get; set; }

    [JsonPropertyName("begin_time")] public required ScheduleTime StartTime { get; set; }

    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("max_floor")] public required string MaxFloor { get; set; }

    [JsonPropertyName("battle_num")] public int BattleNum { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }

    [MemberNotNullWhen(true, nameof(HasData))]
    [JsonPropertyName("all_floor_detail")]
    public List<FloorDetail>? AllFloorDetail { get; set; }

    [JsonPropertyName("max_floor_id")] public int MaxFloorId { get; set; }

    [JsonPropertyName("groups")] public required List<HsrEndGroup> Groups { get; set; }
}

public class NodeInformation
{
    [JsonPropertyName("challenge_time")] public required ScheduleTime ChallengeTime { get; set; }

    [JsonPropertyName("avatars")] public required List<HsrEndAvatar> Avatars { get; set; }
}
