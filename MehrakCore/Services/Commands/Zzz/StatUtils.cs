using System.Runtime.CompilerServices;

namespace MehrakCore.Services.Commands.Zzz;

internal static class StatUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetStatAssetName(string statName)
    {
        return statName.Replace(' ', '_').TrimEnd().ToLowerInvariant();
    }
}
