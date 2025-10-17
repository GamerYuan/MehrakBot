﻿#region

using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Genshin.Types;

public class GenshinTheaterResponseData
{
    [JsonPropertyName("data")] public required List<GenshinTheaterInformation> Data { get; init; }
    [JsonPropertyName("is_unlock")] public bool IsUnlock { get; init; }
}

public class ItAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; init; }

    [JsonPropertyName("avatar_type")] public int AvatarType { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("element")] public string? Element { get; init; }

    [JsonPropertyName("image")] public required string Image { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    public IImageData ToImageData() => new ImageData(string.Format(FileNameFormat.Genshin.AvatarName, AvatarId), Image);
}

public class Buff
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("icon")] public required string Icon { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    public IImageData ToImageData() => new ImageData(string.Format(FileNameFormat.Genshin.BuffIconName, Name.Replace(" ", "")), Icon);
}

public class GenshinTheaterInformation
{
    [JsonPropertyName("detail")] public required Detail Detail { get; init; }

    [JsonPropertyName("stat")] public required Stat Stat { get; init; }

    [JsonPropertyName("schedule")] public required Schedule Schedule { get; init; }

    [JsonPropertyName("has_data")] public bool HasData { get; init; }

    [JsonPropertyName("has_detail_data")] public bool HasDetailData { get; init; }
}

public class Detail
{
    [JsonPropertyName("rounds_data")] public required List<RoundsData> RoundsData { get; init; }

    [JsonPropertyName("detail_stat")] public required DetailStat DetailStat { get; init; }

    [JsonPropertyName("backup_avatars")] public required List<ItAvatar> BackupAvatars { get; init; }

    [JsonPropertyName("fight_statisic")] public required FightStatistic FightStatistic { get; init; }
}

public class DetailStat
{
    [JsonPropertyName("difficulty_id")] public int DifficultyId { get; init; }

    [JsonPropertyName("max_round_id")] public int MaxRoundId { get; init; }

    [JsonPropertyName("heraldry")] public int Heraldry { get; init; }

    [JsonPropertyName("get_medal_round_list")]
    public required List<int> GetMedalRoundList { get; init; }

    [JsonPropertyName("medal_num")] public int MedalNum { get; init; }

    [JsonPropertyName("coin_num")] public int CoinNum { get; init; }

    [JsonPropertyName("avatar_bonus_num")] public int AvatarBonusNum { get; init; }

    [JsonPropertyName("rent_cnt")] public int RentCnt { get; init; }
}

public class EndDateTime
{
    [JsonPropertyName("year")] public int Year { get; init; }

    [JsonPropertyName("month")] public int Month { get; init; }

    [JsonPropertyName("day")] public int Day { get; init; }

    [JsonPropertyName("hour")] public int Hour { get; init; }

    [JsonPropertyName("minute")] public int Minute { get; init; }

    [JsonPropertyName("second")] public int Second { get; init; }
}

public class ItRankAvatar
{
    [JsonPropertyName("avatar_id")] public int AvatarId { get; init; }

    [JsonPropertyName("avatar_icon")] public string? AvatarIcon { get; init; }

    [JsonPropertyName("value")] public required string Value { get; init; }

    [JsonPropertyName("rarity")] public int Rarity { get; init; }

    public IImageData ToImageData() => new ImageData(string.Format(FileNameFormat.Genshin.SideAvatarName, AvatarId), AvatarIcon!);
}

public class FightStatistic
{
    [JsonPropertyName("max_defeat_avatar")]
    public required ItRankAvatar MaxDefeatAvatar { get; init; }

    [JsonPropertyName("max_damage_avatar")]
    public required ItRankAvatar MaxDamageAvatar { get; init; }

    [JsonPropertyName("max_take_damage_avatar")]
    public required ItRankAvatar MaxTakeDamageAvatar { get; init; }

    [JsonPropertyName("total_coin_consumed")]
    public required ItRankAvatar TotalCoinConsumed { get; init; }

    [JsonPropertyName("shortest_avatar_list")]
    public required List<ItRankAvatar> ShortestAvatarList { get; init; }

    [JsonPropertyName("total_use_time")] public int TotalUseTime { get; init; }

    [JsonPropertyName("is_show_battle_stats")]
    public bool IsShowBattleStats { get; init; }
}

public class FinishDateTime
{
    [JsonPropertyName("year")] public int Year { get; init; }

    [JsonPropertyName("month")] public int Month { get; init; }

    [JsonPropertyName("day")] public int Day { get; init; }

    [JsonPropertyName("hour")] public int Hour { get; init; }

    [JsonPropertyName("minute")] public int Minute { get; init; }

    [JsonPropertyName("second")] public int Second { get; init; }
}

public class LevelEffect
{
    [JsonPropertyName("icon")] public required string Icon { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("desc")] public required string Desc { get; init; }
}

public class RoundsData
{
    [JsonPropertyName("avatars")] public required List<ItAvatar> Avatars { get; init; }

    [JsonPropertyName("is_get_medal")] public bool IsGetMedal { get; init; }

    [JsonPropertyName("round_id")] public int RoundId { get; init; }

    [JsonPropertyName("finish_time")] public required string FinishTime { get; init; }

    [JsonPropertyName("finish_date_time")] public required FinishDateTime FinishDateTime { get; init; }

    [JsonPropertyName("splendour_buff")] public SplendourBuff? SplendourBuff { get; init; }

    [JsonPropertyName("is_tarot")] public bool IsTarot { get; init; }
    [JsonPropertyName("tarot_serial_no")] public int TarotSerialNumber { get; init; }
}

public class Schedule
{
    [JsonPropertyName("start_time")] public required string StartTime { get; init; }

    [JsonPropertyName("end_time")] public required string EndTime { get; init; }

    [JsonPropertyName("schedule_type")] public int ScheduleType { get; init; }

    [JsonPropertyName("schedule_id")] public int ScheduleId { get; init; }

    [JsonPropertyName("start_date_time")] public required StartDateTime StartDateTime { get; init; }

    [JsonPropertyName("end_date_time")] public required EndDateTime EndDateTime { get; init; }
}

public class SplendourBuff
{
    [JsonPropertyName("summary")] public required Summary Summary { get; init; }

    [JsonPropertyName("buffs")] public required List<Buff> Buffs { get; init; }
}

public class StartDateTime
{
    [JsonPropertyName("year")] public int Year { get; init; }

    [JsonPropertyName("month")] public int Month { get; init; }

    [JsonPropertyName("day")] public int Day { get; init; }

    [JsonPropertyName("hour")] public int Hour { get; init; }

    [JsonPropertyName("minute")] public int Minute { get; init; }

    [JsonPropertyName("second")] public int Second { get; init; }
}

public class Stat
{
    [JsonPropertyName("difficulty_id")] public int DifficultyId { get; init; }

    [JsonPropertyName("max_round_id")] public int MaxRoundId { get; init; }

    [JsonPropertyName("heraldry")] public int Heraldry { get; init; }

    [JsonPropertyName("get_medal_round_list")]
    public required List<int> GetMedalRoundList { get; init; }

    [JsonPropertyName("medal_num")] public int MedalNum { get; init; }

    [JsonPropertyName("coin_num")] public int CoinNum { get; init; }

    [JsonPropertyName("avatar_bonus_num")] public int AvatarBonusNum { get; init; }

    [JsonPropertyName("rent_cnt")] public int RentCnt { get; init; }
}

public class Summary
{
    [JsonPropertyName("total_level")] public int TotalLevel { get; init; }

    [JsonPropertyName("desc")] public string? Desc { get; init; }
}
