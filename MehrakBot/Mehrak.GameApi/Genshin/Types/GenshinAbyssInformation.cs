#region

using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Genshin.Types;

public class AbyssAvatar
{
    [JsonPropertyName("id")] public int Id { get; init; }

    [JsonPropertyName("icon")] public required string Icon { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    public string ToImageName() => string.Format(FileNameFormat.Genshin.AvatarName, Id);

    public IImageData ToImageData() => new ImageData(ToImageName(), Icon);
}

public class Battle
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("timestamp")] public string? Timestamp { get; init; }

    [JsonPropertyName("avatars")] public required List<AbyssAvatar> Avatars { get; init; }
}

public class GenshinAbyssInformation
{
    [JsonPropertyName("start_time")] public string? StartTime { get; init; }

    [JsonPropertyName("end_time")] public string? EndTime { get; init; }

    [JsonPropertyName("total_battle_times")]
    public int TotalBattleTimes { get; init; }

    [JsonPropertyName("total_win_times")] public int TotalWinTimes { get; init; }

    [JsonPropertyName("max_floor")] public string? MaxFloor { get; init; }

    [JsonPropertyName("reveal_rank")] public List<AbyssRankAvatar>? RevealRank { get; init; }

    [JsonPropertyName("defeat_rank")] public List<AbyssRankAvatar>? DefeatRank { get; init; }

    [JsonPropertyName("damage_rank")] public List<AbyssRankAvatar>? DamageRank { get; init; }

    [JsonPropertyName("take_damage_rank")] public List<AbyssRankAvatar>? TakeDamageRank { get; init; }

    [JsonPropertyName("normal_skill_rank")]
    public List<AbyssRankAvatar>? NormalSkillRank { get; init; }

    [JsonPropertyName("energy_skill_rank")]
    public List<AbyssRankAvatar>? EnergySkillRank { get; init; }

    [JsonPropertyName("floors")] public List<Floor>? Floors { get; init; }

    [JsonPropertyName("total_star")] public int TotalStar { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("is_just_skipped_floor")]
    public bool? IsJustSkippedFloor { get; init; }

    [JsonPropertyName("skipped_floor")] public string? SkippedFloor { get; init; }
}

public class Floor
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("icon")] public string? Icon { get; init; }

    [JsonPropertyName("is_unlock")] public bool? IsUnlock { get; init; }

    [JsonPropertyName("star")] public int Star { get; init; }

    [JsonPropertyName("max_star")] public int MaxStar { get; init; }

    [JsonPropertyName("levels")] public List<Level>? Levels { get; init; }
}

public class Level
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("star")] public int Star { get; init; }

    [JsonPropertyName("max_star")] public int MaxStar { get; init; }

    [JsonPropertyName("battles")] public required List<Battle> Battles { get; init; }
}

public class AbyssRankAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; init; }

    [JsonPropertyName("avatar_icon")] public required string AvatarIcon { get; init; }

    [JsonPropertyName("value")] public int Value { get; init; }

    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    public string ToImageName() =>
        string.Format(FileNameFormat.Genshin.SideAvatarName, AvatarId);

    public string ToAvatarImageName() => string.Format(FileNameFormat.Genshin.AvatarName, AvatarId);

    public IImageData ToImageData() =>
        new ImageData(ToImageName(), AvatarIcon);
}
