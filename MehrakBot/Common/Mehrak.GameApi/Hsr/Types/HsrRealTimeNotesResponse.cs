#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Hsr.Types;

public class HsrRealTimeNotesData
{
    [JsonPropertyName("current_stamina")] public int CurrentStamina { get; init; }

    [JsonPropertyName("max_stamina")] public int MaxStamina { get; init; }

    [JsonPropertyName("stamina_recover_time")]
    public int StaminaRecoverTime { get; init; }

    [JsonPropertyName("stamina_full_ts")] public int StaminaFullTs { get; init; }

    [JsonPropertyName("current_train_score")]
    public int CurrentTrainScore { get; init; }

    [JsonPropertyName("max_train_score")] public int MaxTrainScore { get; init; }

    [JsonPropertyName("current_rogue_score")]
    public int CurrentRogueScore { get; init; }

    [JsonPropertyName("max_rogue_score")] public int MaxRogueScore { get; init; }

    [JsonPropertyName("weekly_cocoon_cnt")]
    public int WeeklyCocoonCnt { get; init; }

    [JsonPropertyName("weekly_cocoon_limit")]
    public int WeeklyCocoonLimit { get; init; }

    [JsonPropertyName("current_reserve_stamina")]
    public int CurrentReserveStamina { get; init; }

    [JsonPropertyName("is_reserve_stamina_full")]
    public bool IsReserveStaminaFull { get; init; }

    [JsonPropertyName("rogue_tourn_weekly_unlocked")]
    public bool RogueTournWeeklyUnlocked { get; init; }

    [JsonPropertyName("rogue_tourn_weekly_max")]
    public int RogueTournWeeklyMax { get; init; }

    [JsonPropertyName("rogue_tourn_weekly_cur")]
    public int RogueTournWeeklyCur { get; init; }

    [JsonPropertyName("current_ts")] public int CurrentTs { get; init; }

    [JsonPropertyName("rogue_tourn_exp_is_full")]
    public bool RogueTournExpIsFull { get; init; }

    [JsonPropertyName("grid_fight_weekly_cur")]
    public int GridFightWeeklyCur { get; init; }

    [JsonPropertyName("grid_fight_weekly_max")]
    public int GridFightWeeklyMax { get; init; }
}
