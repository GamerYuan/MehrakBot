using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mehrak.GameApi.Genshin.Types;

namespace Mehrak.Application.Extensions.Genshin;

public static class GenshinCharacterExtensions
{
    private static readonly int[] AscensionBoundaries = [20, 40, 50, 60, 70, 80];
    private static readonly float[] AscensionMultiplier = [0f, 38f / 182f, 65f / 182f, 101f / 182f, 128f / 182f, 155f / 182f, 1f];
    private static readonly Dictionary<int, float[]> LevelMultiplier = new()
    {
        { 4, [2.569f, 4.220f, 5.046f, 5.872f, 6.697f, 7.523f] },
        { 5, [2.594f, 4.307f, 5.176f, 6.054f, 6.940f, 7.836f] }
    };
    private static readonly int[] AscensionCap = [20, 40, 50, 60, 70, 80, 90];

    public static bool TryGetAscensionLevelCap(this GenshinCharacterInformation charData, float? baseVal,
        float? maxAscVal, [NotNullWhen(true)] out int? ascLevelCap)
    {
        ascLevelCap = default;

        if (charData.Base.Level > 90)
        {
            ascLevelCap = charData.Base.Level;
            return true;
        }

        if (!AscensionBoundaries.Contains(charData.Base.Level))
        {
            if (charData.Base.Level < 20)
            {
                ascLevelCap = 20;
                return true;
            }

            var i = AscensionBoundaries.Length - 1;
            while (i >= 0)
            {
                if (charData.Base.Level > AscensionBoundaries[i]) break;
                i--;
            }

            ascLevelCap = AscensionCap[i + 1];
            return true;
        }

        if (baseVal is null || maxAscVal is null) return false;

        var boundary = AscensionBoundaries.IndexOf(charData.Base.Level);

        var statProp = charData.BaseProperties.FirstOrDefault(x => x.PropertyType == 2000);
        if (statProp is null || !LevelMultiplier.TryGetValue(charData.Base.Rarity, out var multipliers))
            return false;

        if (!float.TryParse(statProp.Base, CultureInfo.InvariantCulture, out var statVal))
            return false;

        var ascVal = statVal - baseVal * multipliers[boundary];

        if (ascVal < 0) return false;

        var ascMult = ascVal / maxAscVal;

        var closestIndex = -1;
        var minDiff = float.MaxValue;

        for (var i = 0; i < AscensionMultiplier.Length; i++)
        {
            var diff = Math.Abs(AscensionMultiplier[i] - ascMult.Value);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestIndex = i;
            }
        }

        if (minDiff <= 0.05f)
        {
            ascLevelCap = AscensionCap[closestIndex];
            return true;
        }

        return false;
    }
}
