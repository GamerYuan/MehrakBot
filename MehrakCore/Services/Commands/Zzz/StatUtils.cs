namespace MehrakCore.Services.Commands.Zzz;

internal static class StatUtils
{
    internal static string GetStatAssetName(string statName)
    {
        return statName.Replace(' ', '_').TrimEnd().ToLowerInvariant().Insert(0, "zzz_stats_");
    }
}
