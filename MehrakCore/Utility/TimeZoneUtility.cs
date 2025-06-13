namespace MehrakCore.Utility;

public static class TimeZoneUtility
{
    public static TimeZoneInfo GetTimeZoneInfo(this Regions region)
    {
        return region switch
        {
            Regions.America => TimeZoneInfo.CreateCustomTimeZone("America_Server", TimeSpan.FromHours(-5),
                "America Server", "America Server"),
            Regions.Europe => TimeZoneInfo.CreateCustomTimeZone("Europe_Server", TimeSpan.FromHours(1), "Europe Server",
                "Europe Server"),
            Regions.Asia => TimeZoneInfo.CreateCustomTimeZone("Asia_Server", TimeSpan.FromHours(8), "Asia Server",
                "Asia Server"),
            Regions.Sar => TimeZoneInfo.CreateCustomTimeZone("Asia_Server", TimeSpan.FromHours(8), "Asia Server",
                "Asia Server"),
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null)
        };
    }
}
