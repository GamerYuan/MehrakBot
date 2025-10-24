using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Utility;

public static class ServerUtility
{
    public static TimeZoneInfo GetTimeZoneInfo(this Server region)
    {
        return region switch
        {
            Server.America => TimeZoneInfo.CreateCustomTimeZone("America_Server", TimeSpan.FromHours(-5),
                "America Server", "America Server"),
            Server.Europe => TimeZoneInfo.CreateCustomTimeZone("Europe_Server", TimeSpan.FromHours(1), "Europe Server",
                "Europe Server"),
            Server.Asia => TimeZoneInfo.CreateCustomTimeZone("Asia_Server", TimeSpan.FromHours(8), "Asia Server",
                "Asia Server"),
            Server.Sar => TimeZoneInfo.CreateCustomTimeZone("Asia_Server", TimeSpan.FromHours(8), "Asia Server",
                "Asia Server"),
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, null)
        };
    }

    public static long GetNextWeeklyResetUnix(this Server region)
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
