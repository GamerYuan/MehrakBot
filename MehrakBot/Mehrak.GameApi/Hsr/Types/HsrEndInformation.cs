#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Hsr.Types;

public class HsrEndFloorDetail
{
    [JsonPropertyName("name")] public string Name { get; set; }

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

    [JsonPropertyName("icon")] public string Icon { get; set; }

    [JsonPropertyName("rarity")] public int Rarity { get; set; }

    [JsonPropertyName("element")] public string Element { get; set; }

    [JsonPropertyName("rank")] public int Rank { get; set; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.AvatarName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class HsrEndBuff
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name_mi18n")] public string NameMi18N { get; set; }

    [JsonPropertyName("desc_mi18n")] public string DescMi18N { get; set; }

    [JsonPropertyName("icon")] public string Icon { get; set; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Hsr.EndGameBuffName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class HsrEndGroup
{
    [JsonPropertyName("schedule_id")] public int ScheduleId { get; set; }

    [JsonPropertyName("begin_time")] public ScheduleTime BeginTime { get; set; }

    [JsonPropertyName("end_time")] public ScheduleTime EndTime { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("name_mi18n")] public string Name { get; set; }
}

public class HsrEndNodeInformation
{
    [JsonPropertyName("challenge_time")] public ScheduleTime ChallengeTime { get; set; }

    [JsonPropertyName("avatars")] public List<HsrEndAvatar> Avatars { get; set; }

    [JsonPropertyName("buff")] public HsrEndBuff Buff { get; set; }

    [JsonPropertyName("score")] public string Score { get; set; }

    /// <summary>
    /// Only used for Apocalyptic Shadow
    /// </summary>
    [JsonPropertyName("boss_defeated")]
    public bool BossDefeated { get; set; }
}

public class HsrEndInformation
{
    [JsonPropertyName("groups")] public List<HsrEndGroup> Groups { get; set; }

    [JsonPropertyName("star_num")] public int StarNum { get; set; }

    [JsonPropertyName("max_floor")] public string MaxFloor { get; set; }

    [JsonPropertyName("battle_num")] public int BattleNum { get; set; }

    [JsonPropertyName("has_data")] public bool HasData { get; set; }

    [JsonPropertyName("all_floor_detail")] public List<HsrEndFloorDetail> AllFloorDetail { get; set; }

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
        return new DateTime(Year, Month, Day, Hour, Minute, 0, DateTimeKind.Unspecified);
    }
}
