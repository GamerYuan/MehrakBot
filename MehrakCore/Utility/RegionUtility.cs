namespace MehrakCore.Utility;

public static class RegionUtility
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

    public static long GetNextWeeklyResetUnix(this Regions region)
    {
        var tz = region.GetTimeZoneInfo();
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

        // Calculate days until next Monday
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && nowLocal.TimeOfDay >= TimeSpan.FromHours(4))
            daysUntilMonday = 7; // If it's already Monday after 4AM, go to next week

        var nextMondayLocal = nowLocal.Date.AddDays(daysUntilMonday).AddHours(4);

        // Convert back to UTC
        return new DateTimeOffset(nextMondayLocal).ToUnixTimeSeconds();
    }
}
