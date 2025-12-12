using System.Text.Json.Serialization;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Hsr.Types;

public class HsrAnomalyInformation
{
    [JsonPropertyName("challenge_peak_records")] public required List<ChallengeRecord> ChallengeRecords { get; init; }
    [JsonPropertyName("challenge_peak_best_record_brief")] public required RecordBrief BestRecord { get; init; }

    public string ToMedalName() => string.Format(FileNameFormat.Hsr.AnomalyName,
        $"icon_{BestRecord.RankIconType}_{ChallengeRecords[0].Group.GameVersion.Replace('.', '_')}");

    public IImageData ToMedalIconData() => new ImageData(ToMedalName(), BestRecord.RankIcon);

}

public class AnomalyGroup
{
    [JsonPropertyName("group_id")] public int GroupId { get; init; }
    [JsonPropertyName("begin_time")] public required ScheduleTime BeginTime { get; init; }
    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; init; }
    [JsonPropertyName("name_mi18n")] public required string Name { get; init; }
    [JsonPropertyName("theme_pic_path")] public required string ThemePicPath { get; init; }
    [JsonPropertyName("game_version")] public required string GameVersion { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Hsr.AnomalyName, $"theme_{GameVersion}");

    public IImageData ToImageData() => new ImageData(ToImageName(), ThemePicPath);
}

public class ChallengeRecord
{
    [JsonPropertyName("group")] public required AnomalyGroup Group { get; init; }
    [JsonPropertyName("boss_info")] public required BossInfo BossInfo { get; init; }
    [JsonPropertyName("mob_infos")] public required List<MobInfo> MobInfo { get; init; }
    [JsonPropertyName("boss_record")] public BossRecord? BossRecord { get; init; }
    [JsonPropertyName("mob_records")] public required List<MobRecord> MobRecords { get; init; }
    [JsonPropertyName("has_challenge_record")] public bool HasChallengeRecord { get; init; }
    [JsonPropertyName("battle_num")] public int BattleNum { get; init; }
    [JsonPropertyName("boss_stars")] public int BossStars { get; init; }
    [JsonPropertyName("mob_stars")] public int MobStars { get; init; }
}

public class BossInfo
{
    [JsonPropertyName("maze_id")] public int MazeId { get; init; }
    [JsonPropertyName("name_mi18n")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Hsr.FileName,
        string.Join('_', Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", ""));

    public IImageData ToImageData() => new ImageData(ToImageName(), Icon);
}

public class MobInfo
{
    [JsonPropertyName("maze_id")] public int MazeId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("monster_name")] public required string MonsterName { get; init; }
}

public class MobRecord
{
    [JsonPropertyName("maze_id")] public int MazeId { get; init; }
    [JsonPropertyName("has_challenge_record")] public bool HasChallengeRecord { get; init; }
    [JsonPropertyName("avatars")] public required List<HsrEndAvatar> Avatars { get; init; }
    [JsonPropertyName("round_num")] public int RoundNum { get; init; }
    [JsonPropertyName("star_num")] public int StarNum { get; init; }
    [JsonPropertyName("is_fast")] public bool IsFast { get; init; }
}

public class BossRecord : MobRecord
{
    [JsonPropertyName("buff")] public required HsrEndBuff Buff { get; init; }
    [JsonPropertyName("hard_mode")] public bool HardMode { get; init; }
    [JsonPropertyName("finish_color_medal")] public bool FinishColorMedal { get; init; }
    [JsonPropertyName("challenge_peak_rank_icon_type")] public required string RankIconType { get; init; }
    [JsonPropertyName("challenge_peak_rank_icon")] public required string RankIcon { get; init; }
}

public class RecordBrief
{
    [JsonPropertyName("total_battle_num")] public int TotalBattleNum { get; init; }
    [JsonPropertyName("boss_stars")] public int BossStars { get; init; }
    [JsonPropertyName("mob_stars")] public int MobStars { get; init; }

    [JsonPropertyName("challenge_peak_rank_icon_type"), JsonConverter(typeof(JsonStringEnumConverter))]
    public RankIconType RankIconType { get; init; }
    [JsonPropertyName("challenge_peak_rank_icon")] public required string RankIcon { get; init; }
}

public enum RankIconType
{
    ChallengePeakRankIconTypeNone,
    ChallengePeakRankIconTypeBronze,
    ChallengePeakRankIconTypeSilver,
    ChallengePeakRankIconTypeGold,
    ChallengePeakRankIconTypeUltra
}
