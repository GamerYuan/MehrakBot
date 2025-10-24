using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Zzz.Types;

public class ZzzRealTimeNotesData
{
    [JsonPropertyName("energy")] public required EnergyInfo Energy { get; init; }
    [JsonPropertyName("vitality")] public required RegenProgress Vitality { get; init; }
    [JsonPropertyName("vhs_sale")] public required VhsSale VhsSale { get; init; }

    [JsonPropertyName("card_sign"), JsonConverter(typeof(JsonStringEnumConverter<CardSignState>))]
    public CardSignState CardSign { get; init; }

    [JsonPropertyName("bounty_commission")]
    public required BountyCommissionInfo BountyCommission { get; init; }

    [JsonPropertyName("weekly_task")] public WeeklyTaskInfo? WeeklyTask { get; init; }
    [JsonPropertyName("temple_running")] public required TempleManageInfo TempleManage { get; init; }
}

public class EnergyInfo
{
    [JsonPropertyName("progress")] public required RegenProgress Progress { get; init; }
    [JsonPropertyName("restore")] public int Restore { get; init; }
    [JsonPropertyName("day_type")] public int DayType { get; init; }
    [JsonPropertyName("hour")] public int Hour { get; init; }
    [JsonPropertyName("min")] public int Min { get; init; }
}

public class RegenProgress
{
    [JsonPropertyName("max")] public int Max { get; init; }
    [JsonPropertyName("current")] public int Current { get; init; }
}

public class VhsSale
{
    [JsonPropertyName("sale_state"), JsonConverter(typeof(JsonStringEnumConverter<VhsSaleState>))]
    public VhsSaleState SaleState { get; init; }
}

public class BountyCommissionInfo
{
    [JsonPropertyName("num")] public int Num { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("refresh_time")] public int RefreshTime { get; init; }
}

public class WeeklyTaskInfo
{
    [JsonPropertyName("cur_point")] public int CurPoint { get; init; }
    [JsonPropertyName("max_point")] public int MaxPoint { get; init; }
    [JsonPropertyName("refresh_time")] public int RefreshTime { get; init; }
}

public class TempleManageInfo
{
    [JsonPropertyName("expedition_state"), JsonConverter(typeof(JsonStringEnumConverter<ExpeditionState>))]
    public ExpeditionState ExpeditionState { get; init; }

    [JsonPropertyName("bench_state"), JsonConverter(typeof(JsonStringEnumConverter<BenchState>))]
    public BenchState BenchState { get; init; }

    [JsonPropertyName("shelve_state"), JsonConverter(typeof(JsonStringEnumConverter<ShelveState>))]
    public ShelveState ShelveState { get; init; }

    [JsonPropertyName("level")] public int Level { get; init; }

    [JsonPropertyName("weekly_currency_max")]
    public int WeeklyCurrencyMax { get; init; }

    [JsonPropertyName("currency_next_refresh_ts")]
    public int CurrencyNextRefreshTs { get; init; }

    [JsonPropertyName("current_currency")] public int CurrentCurrency { get; init; }
}

public enum VhsSaleState
{
    SaleStateNo,
    SaleStateDoing,
    SaleStateDone
}

public enum CardSignState
{
    CardSignNo,
    CardSignDone
}

public enum ExpeditionState
{
    ExpeditionStateInProgress,
    ExpeditionStateInCanSend,
    ExpeditionStateEnd
}

public enum BenchState
{
    BenchStateProducing,
    BenchStateCanProduce
}

public enum ShelveState
{
    ShelveStateSelling,
    ShelveStateSoldOut,
    ShelveStateCanSell
}
