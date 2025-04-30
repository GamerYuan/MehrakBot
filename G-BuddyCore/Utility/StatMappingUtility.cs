#region

using System.Collections.ObjectModel;

#endregion

namespace G_BuddyCore.Utility;

public static class StatMappingUtility
{
    public static IReadOnlyDictionary<int, string> Mapping { get; }

    static StatMappingUtility()
    {
        Mapping = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
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
    }
}
