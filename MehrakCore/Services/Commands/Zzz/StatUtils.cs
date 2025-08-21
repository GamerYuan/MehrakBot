using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MehrakCore.Services.Commands.Zzz;

internal static partial class StatUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetStatAssetName(string statName)
    {
        return DamageBonusRegex().Replace(statName, "").Replace(' ', '_').TrimEnd().ToLowerInvariant();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetSpecialAttributeName(int subElementId)
    {
        return subElementId switch
        {
            1 => "frost",
            2 => "auricink",
            _ => throw new ArgumentOutOfRangeException(nameof(subElementId),
                subElementId, null)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetElementNameFromId(int elementId)
    {
        return elementId switch
        {
            200 => "physical",
            201 => "fire",
            202 => "ice",
            203 => "electric",
            205 => "ether",
            _ => throw new ArgumentOutOfRangeException(nameof(elementId), elementId, null)
        };
    }

    [GeneratedRegex(@"\sDMG\sBonus")]
    private static partial Regex DamageBonusRegex();
}
