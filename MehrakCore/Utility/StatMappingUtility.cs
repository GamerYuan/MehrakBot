#region

using System.Collections.ObjectModel;
using MehrakCore.Models;

#endregion

namespace MehrakCore.Utility;

public static class StatMappingUtility
{
    public static IReadOnlyDictionary<int, string> GenshinMapping { get; }
    private static readonly IReadOnlyDictionary<int, float> GenshinDefaultValues;

    public static IReadOnlyDictionary<int, string> HsrMapping { get; }
    private static readonly IReadOnlyDictionary<int, float> HsrDefaultValues;

    static StatMappingUtility()
    {
        GenshinMapping = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            { 1, "HP" },
            { 2, "HP" },
            { 3, "HP Percentage" },
            { 4, "Base ATK" },
            { 5, "ATK" },
            { 6, "ATK Percentage" },
            { 7, "DEF" },
            { 8, "DEF" },
            { 9, "DEF Percentage" },
            { 20, "CRIT Rate" },
            { 22, "CRIT DMG" },
            { 23, "Energy Recharge" },
            { 26, "Healing Bonus" },
            { 27, "Incoming Healing Bonus" },
            { 28, "Elemental Mastery" },
            { 29, "Physical RES" },
            { 30, "Physical DMG Bonus" },
            { 40, "Pyro DMG Bonus" },
            { 41, "Electro DMG Bonus" },
            { 42, "Hydro DMG Bonus" },
            { 43, "Dendro DMG Bonus" },
            { 44, "Anemo DMG Bonus" },
            { 45, "Geo DMG Bonus" },
            { 46, "Cryo DMG Bonus" },
            { 50, "Pyro RES" },
            { 51, "Electro RES" },
            { 52, "Hydro RES" },
            { 53, "Dendro RES" },
            { 54, "Anemo RES" },
            { 55, "Geo RES" },
            { 56, "Cryo RES" },
            { 80, "CD Reduction" },
            { 81, "Shield Strength" },
            { 2000, "Max HP" },
            { 2001, "ATK" },
            { 2002, "DEF" },
            { 999999, "Max Stamina" }
        });

        HsrMapping = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            { 1, "HP" },
            { 2, "ATK" },
            { 3, "DEF" },
            { 4, "SPD" },
            { 5, "CRIT Rate" },
            { 6, "CRIT DMG" },
            { 7, "Outgoing Healing Boost" },
            { 9, "Energy Regeneration Rate" },
            { 10, "Effect Hit Rate" },
            { 11, "Effect RES" },
            { 12, "Physical DMG Boost" },
            { 13, "Physical RES Boost" },
            { 14, "Fire DMG Boost" },
            { 15, "Fire RES Boost" },
            { 16, "Ice DMG Boost" },
            { 17, "Ice RES Boost" },
            { 18, "Lightning DMG Boost" },
            { 19, "Lightning RES Boost" },
            { 20, "Wind DMG Boost" },
            { 21, "Wind RES Boost" },
            { 22, "Quantum DMG Boost" },
            { 23, "Quantum RES Boost" },
            { 24, "Imaginary DMG Boost" },
            { 25, "Imaginary RES Boost" },
            { 26, "Base HP" },
            { 27, "HP" },
            { 28, "Base ATK" },
            { 29, "ATK" },
            { 30, "Base DEF" },
            { 31, "DEF" },
            { 32, "HP" },
            { 33, "ATK" },
            { 34, "DEF" },
            { 35, "SPD" },
            { 36, "Outgoing Healing Boost" },
            { 37, "Physical RES Boost" },
            { 38, "Fire RES Boost" },
            { 39, "Ice RES Boost" },
            { 40, "Lightning RES Boost" },
            { 41, "Wind RES Boost" },
            { 42, "Quantum RES Boost" },
            { 43, "Imaginary RES Boost" },
            { 51, "SPD" },
            { 52, "CRIT Rate" },
            { 53, "CRIT DMG" },
            { 54, "Energy Regeneration Rate" },
            { 55, "Outgoing Healing Boost" },
            { 56, "Effect Hit Rate" },
            { 57, "Effect RES" },
            { 58, "Break Effect" },
            { 59, "Break Effect" },
            { 60, "Max Energy" }
        });

        GenshinDefaultValues = new ReadOnlyDictionary<int, float>(new Dictionary<int, float>
        {
            { 20, 5 },
            { 22, 50 },
            { 23, 100 }
        });

        HsrDefaultValues = new ReadOnlyDictionary<int, float>(new Dictionary<int, float>
        {
            { 5, 5 },
            { 6, 50 },
            { 9, 100 },
            { 52, 5 },
            { 53, 50 }
        });
    }

    public static float GetDefaultValue(int propertyType, GameName gameName)
    {
        return gameName switch
        {
            GameName.Genshin => GenshinDefaultValues.GetValueOrDefault(propertyType, 0),
            GameName.HonkaiStarRail => HsrDefaultValues.GetValueOrDefault(propertyType, 0),
            _ => throw new InvalidOperationException("Unsupported game name.")
        };
    }

    public static bool IsBaseStat(int propertyType)
    {
        return propertyType is 2000 or 2001 or 2002;
    }
}
