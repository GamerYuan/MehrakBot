#region

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

#endregion

namespace Mehrak.Application.Services.Zzz;

internal static partial class StatUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetStatAssetName(string statName)
    {
        return DamageBonusRegex().Replace(statName, "").Replace("Base ", "").Replace(' ', '_').TrimEnd()
            .ToLowerInvariant();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetElementNameFromId(int elementId, int subElementId)
    {
        if (subElementId == 0)
            return elementId switch
            {
                200 => "physical",
                201 => "fire",
                202 => "ice",
                203 => "electric",
                205 => "ether",
                _ => throw new ArgumentOutOfRangeException(nameof(elementId), elementId, null)
            };
        else
            return subElementId switch
            {
                1 => "frost",
                2 => "auricink",
                _ => throw new ArgumentOutOfRangeException(nameof(subElementId),
                    subElementId, null)
            };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetProfessionNameFromId(int professionId)
    {
        return professionId switch
        {
            1 => "Attack",
            2 => "Stun",
            3 => "Anomaly",
            4 => "Support",
            5 => "Defense",
            6 => "Rupture",
            _ => "Unknown"
        };
    }

    [GeneratedRegex(@"\sDMG\sBonus")]
    private static partial Regex DamageBonusRegex();
}
