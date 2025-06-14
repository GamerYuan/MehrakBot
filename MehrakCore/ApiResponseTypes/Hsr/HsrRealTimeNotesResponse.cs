// Root myDeserializedClass = JsonSerializer.Deserialize<Root>(myJsonResponse);

#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.ApiResponseTypes.Hsr;

public class HsrRealTimeNotesData : IRealTimeNotesData
{
    [JsonPropertyName("current_stamina")] public int CurrentStamina { get; init; }

    [JsonPropertyName("max_stamina")] public int MaxStamina { get; init; }

    [JsonPropertyName("stamina_recover_time")]
    public int StaminaRecoverTime { get; init; }

    [JsonPropertyName("stamina_full_ts")] public int StaminaFullTs { get; init; }

    [JsonPropertyName("accepted_epedition_num")]
    public int AcceptedExpeditionNum { get; init; }

    [JsonPropertyName("total_expedition_num")]
    public int TotalExpeditionNum { get; init; }

    [JsonPropertyName("expeditions")] public List<Expedition>? Expeditions { get; init; }

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
}

public class Expedition
{
    [JsonPropertyName("avatars")] public List<string>? Avatars { get; init; }

    [JsonPropertyName("status")] public string? Status { get; init; }

    [JsonPropertyName("remaining_time")] public int RemainingTime { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("item_url")] public string? ItemUrl { get; init; }

    [JsonPropertyName("finish_ts")] public int FinishTs { get; init; }
}