using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Hsr.Types;

public class HsrAnomalyInformation
{
}

public class AnomalyGroup
{
    [JsonPropertyName("group_id")] public int GroupId { get; init; }
    [JsonPropertyName("begin_time")] public required ScheduleTime BeginTime { get; init; }
    [JsonPropertyName("end_time")] public required ScheduleTime EndTime { get; init; }
    [JsonPropertyName("name_mi18n")] public required string Name { get; init; }
    [JsonPropertyName("theme_pic_path")] public required string ThemePicPath { get; init; }
}

public class ChallengeRecord
{
    [JsonPropertyName("group")] public required AnomalyGroup Group { get; init; }
    [JsonPropertyName("boss_info")] public required BossInfo BossInfo { get; init; }
    [JsonPropertyName("mob_infos")] public required List<MobInfo> MobInfo { get; init; }
    [JsonPropertyName("has_challenge_record")] public bool HasChallengeRecord { get; init; }
    [JsonPropertyName("battle_num")] public int BattleNum { get; init; }

}

public class BossInfo
{
    [JsonPropertyName("maze_id")] public int MazeId { get; init; }
    [JsonPropertyName("name_mi18n")] public required string Name { get; init; }
    [JsonPropertyName("icon")] public required string Icon { get; init; }
}

public class MobInfo
{
    [JsonPropertyName("maze_id")] public int MazeId { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("monster_name")] public required string MonsterName { get; init; }
    [JsonPropertyName("monster_icon")] public required string MonsterIcon { get; init; }
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
}
