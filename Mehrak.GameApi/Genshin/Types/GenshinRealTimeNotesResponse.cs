#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Genshin.Types;

// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);
public class ArchonQuestProgress
{
    [JsonPropertyName("list")] public List<object>? List { get; init; }

    [JsonPropertyName("is_open_archon_quest")]
    public bool IsOpenArchonQuest { get; init; }

    [JsonPropertyName("is_finish_all_mainline")]
    public bool IsFinishAllMainline { get; init; }

    [JsonPropertyName("is_finish_all_interchapter")]
    public bool IsFinishAllInterchapter { get; init; }

    [JsonPropertyName("wiki_url")] public string? WikiUrl { get; init; }
}

public class AttendanceReward
{
    [JsonPropertyName("status")] public string? Status { get; init; }

    [JsonPropertyName("progress")] public int Progress { get; init; }
}

public class DailyTask
{
    [JsonPropertyName("total_num")] public int TotalNum { get; init; }

    [JsonPropertyName("finished_num")] public int FinishedNum { get; init; }

    [JsonPropertyName("is_extra_task_reward_received")]
    public bool IsExtraTaskRewardReceived { get; init; }

    [JsonPropertyName("task_rewards")] public List<TaskReward>? TaskRewards { get; init; }

    [JsonPropertyName("attendance_rewards")]
    public List<AttendanceReward>? AttendanceRewards { get; init; }

    [JsonPropertyName("attendance_visible")]
    public bool AttendanceVisible { get; init; }

    [JsonPropertyName("stored_attendance")]
    public string? StoredAttendance { get; init; }

    [JsonPropertyName("stored_attendance_refresh_countdown")]
    public int StoredAttendanceRefreshCountdown { get; init; }
}

public class GenshinRealTimeNotesData : IRealTimeNotesData
{
    [JsonPropertyName("current_resin")] public int CurrentResin { get; init; }

    [JsonPropertyName("max_resin")] public int MaxResin { get; init; }

    [JsonPropertyName("resin_recovery_time")]
    public string? ResinRecoveryTime { get; init; }

    [JsonPropertyName("finished_task_num")]
    public int FinishedTaskNum { get; init; }

    [JsonPropertyName("total_task_num")] public int TotalTaskNum { get; init; }

    [JsonPropertyName("is_extra_task_reward_received")]
    public bool IsExtraTaskRewardReceived { get; init; }

    [JsonPropertyName("remain_resin_discount_num")]
    public int RemainResinDiscountNum { get; init; }

    [JsonPropertyName("resin_discount_num_limit")]
    public int ResinDiscountNumLimit { get; init; }

    [JsonPropertyName("current_expedition_num")]
    public int CurrentExpeditionNum { get; init; }

    [JsonPropertyName("max_expedition_num")]
    public int MaxExpeditionNum { get; init; }

    [JsonPropertyName("expeditions")] public List<Expedition>? Expeditions { get; init; }

    [JsonPropertyName("current_home_coin")]
    public int CurrentHomeCoin { get; init; }

    [JsonPropertyName("max_home_coin")] public int MaxHomeCoin { get; init; }

    [JsonPropertyName("home_coin_recovery_time")]
    public string? HomeCoinRecoveryTime { get; init; }

    [JsonPropertyName("calendar_url")] public string? CalendarUrl { get; init; }

    [JsonPropertyName("transformer")] public Transformer? Transformer { get; init; }

    [JsonPropertyName("daily_task")] public DailyTask? DailyTask { get; init; }

    [JsonPropertyName("archon_quest_progress")]
    public ArchonQuestProgress? ArchonQuestProgress { get; init; }
}

public class Expedition
{
    [JsonPropertyName("avatar_side_icon")] public string? AvatarSideIcon { get; init; }

    [JsonPropertyName("status")] public string? Status { get; init; }

    [JsonPropertyName("remained_time")] public string? RemainedTime { get; init; }
}

public class RecoveryTime
{
    [JsonPropertyName("Day")] public int Day { get; init; }

    [JsonPropertyName("Hour")] public int Hour { get; init; }

    [JsonPropertyName("Minute")] public int Minute { get; init; }

    [JsonPropertyName("Second")] public int Second { get; init; }

    [JsonPropertyName("reached")] public bool Reached { get; init; }
}

public class TaskReward
{
    [JsonPropertyName("status")] public string? Status { get; init; }
}

public class Transformer
{
    [JsonPropertyName("obtained")] public bool Obtained { get; init; }

    [JsonPropertyName("recovery_time")] public RecoveryTime? RecoveryTime { get; init; }

    [JsonPropertyName("wiki")] public string? Wiki { get; init; }

    [JsonPropertyName("noticed")] public bool Noticed { get; init; }

    [JsonPropertyName("latest_job_id")] public string? LatestJobId { get; init; }
}