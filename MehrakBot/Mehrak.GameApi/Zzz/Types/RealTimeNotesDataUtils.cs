namespace Mehrak.GameApi.Zzz.Types;

public static class RealTimeNotesDataUtils
{
    public static string ToReadableString(this VhsSaleState state) => state switch
    {
        VhsSaleState.SaleStateNo => "Waiting to Open",
        VhsSaleState.SaleStateDoing => "Currently Open",
        VhsSaleState.SaleStateDone => "Revenue Available",
        _ => "Unknown"
    };

    public static string ToReadableString(this CardSignState state) => state switch
    {
        CardSignState.CardSignNo => "Incomplete",
        CardSignState.CardSignDone => "Completed",
        _ => "Unknown"
    };

    public static string ToReadableString(this ExpeditionState state) => state switch
    {
        ExpeditionState.ExpeditionStateInCanSend => "Dispatchable",
        ExpeditionState.ExpeditionStateInProgress => "Adventuring",
        ExpeditionState.ExpeditionStateEnd => "Completed",
        _ => "Unknown"
    };

    public static string ToReadableString(this BenchState state) => state switch
    {
        BenchState.BenchStateProducing => "Crafting",
        BenchState.BenchStateCanProduce => "Craftable",
        _ => "Unknown"
    };

    public static string ToReadableString(this ShelveState state) => state switch
    {
        ShelveState.ShelveStateCanSell => "Not Selling",
        ShelveState.ShelveStateSelling => "Selling",
        ShelveState.ShelveStateSoldOut => "Out of Stock",
        _ => "Unknown"
    };
}
