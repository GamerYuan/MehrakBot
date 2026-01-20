#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

#endregion

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzChallengeAvatar
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }
    [JsonPropertyName("element_type")] public int ElementType { get; init; }

    [JsonPropertyName("avatar_profession")]
    public int AvatarProfession { get; init; }

    [JsonPropertyName("rarity")] public required string Rarity { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("role_square_url")] public required string RoleSquareUrl { get; init; }
    [JsonPropertyName("sub_element_type")] public int SubElementType { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.AvatarName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), RoleSquareUrl);
    }
}

public class ZzzBuddy
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("rarity")] public required string Rarity { get; init; }
    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("bangboo_rectangle_url")]
    public required string BangbooRectangleUrl { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.BuddyName, Id);
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), BangbooRectangleUrl);
    }
}

public class ZzzDefenseData
{
    [JsonPropertyName("begin_time")] public required string BeginTime { get; init; }
    [JsonPropertyName("end_time")] public required string EndTime { get; init; }
    [JsonPropertyName("rating_list")] public required List<RatingData> RatingList { get; init; }
    [JsonPropertyName("has_data")] public bool HasData { get; init; }
    [JsonPropertyName("all_floor_detail")] public required List<FloorDetail> AllFloorDetail { get; init; }
}

public class ZzzAssaultData
{
    [JsonPropertyName("start_time")] public required ScheduleTime StartTime { get; init; }
    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; init; }
    [JsonPropertyName("rank_percent")] public int RankPercent { get; init; }
    [JsonPropertyName("list")] public required List<AssaultFloorDetail> List { get; init; }
    [JsonPropertyName("has_data")] public bool HasData { get; init; }
    [JsonPropertyName("total_score")] public int TotalScore { get; init; }
    [JsonPropertyName("total_star")] public int TotalStar { get; init; }
}

public class RatingData
{
    [JsonPropertyName("times")] public int Times { get; init; }
    [JsonPropertyName("rating")] public required string Rating { get; init; }
}

public class FloorDetail
{
    [JsonPropertyName("layer_index")] public int LayerIndex { get; init; }
    [JsonPropertyName("rating")] public required string Rating { get; init; }
    [JsonPropertyName("node_1")] public required NodeData Node1 { get; init; }
    [JsonPropertyName("node_2")] public required NodeData Node2 { get; init; }
    [JsonPropertyName("zone_name")] public required string ZoneName { get; init; }
}

public class NodeData
{
    [JsonPropertyName("avatars")] public required List<ZzzChallengeAvatar> Avatars { get; init; }
    [JsonPropertyName("buddy")] public required ZzzBuddy? Buddy { get; init; }
    [JsonPropertyName("battle_time")] public int BattleTime { get; init; }
}

public class AssaultFloorDetail
{
    [JsonPropertyName("score")] public int Score { get; init; }
    [JsonPropertyName("star")] public int Star { get; init; }
    [JsonPropertyName("boss")] public required List<AssaultBoss> Boss { get; init; }
    [JsonPropertyName("avatar_list")] public required List<ZzzChallengeAvatar> AvatarList { get; init; }
    [JsonPropertyName("buffer")] public required List<AssaultBuff> Buff { get; init; }
    [JsonPropertyName("buddy")] public required ZzzBuddy? Buddy { get; init; }
}

public class AssaultBoss
{
    [JsonPropertyName("icon")] public required string Icon { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("bg_icon")] public required string BgIcon { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.AssaultBossName,
            string.Join('_', Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", ""));
    }

    public IMultiImageData ToImageData()
    {
        return new MultiImageData(ToImageName(), [BgIcon, Icon]);
    }
}

public class AssaultBuff
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }

    public string ToImageName()
    {
        return string.Format(FileNameFormat.Zzz.AssaultBuffName,
            string.Join('_', Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", ""));
    }

    public IImageData ToImageData()
    {
        return new ImageData(ToImageName(), Icon);
    }
}

public class ScheduleTime
{
    [JsonPropertyName("year")] public int Year { get; init; }
    [JsonPropertyName("month")] public int Month { get; init; }
    [JsonPropertyName("day")] public int Day { get; init; }
    [JsonPropertyName("hour")] public int Hour { get; init; }
    [JsonPropertyName("minute")] public int Minute { get; init; }
    [JsonPropertyName("second")] public int Second { get; init; }

    public long ToTimestamp(TimeZoneInfo tz)
    {
        DateTime dt = new(Year, Month, Day, Hour, Minute, Second, DateTimeKind.Unspecified);
        DateTimeOffset dto = TimeZoneInfo.ConvertTimeToUtc(dt, tz);
        return dto.ToUnixTimeSeconds();
    }
}
