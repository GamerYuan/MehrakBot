#region

using System.Globalization;

#endregion

namespace Mehrak.GameApi.Shared;

/// <summary>
/// Structure checks shared by game-record endpoints that must reject malformed required payload members before the
/// command layer consumes them.
/// </summary>
internal static class GameApiPayloadValidation
{
    private const long MinUnixSeconds = -62135596800;
    private const long MaxUnixSeconds = 253402300799;

    public static bool HasNoNullEntries<T>(IEnumerable<T>? values) where T : class
    {
        return values is not null && values.All(value => value is not null);
    }

    public static bool IsUnixSeconds(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) &&
               seconds is >= MinUnixSeconds and <= MaxUnixSeconds;
    }

    public static bool IsValidCalendarTime(int year, int month, int day, int hour, int minute)
    {
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 &&
               day >= 1 && day <= DateTime.DaysInMonth(year, month) &&
               hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }

    public static bool IsValidCalendarTime(int year, int month, int day, int hour, int minute, int second)
    {
        return IsValidCalendarTime(year, month, day, hour, minute) && second is >= 0 and <= 59;
    }
}
