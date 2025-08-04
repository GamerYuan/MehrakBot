#region

using System.Text;

#endregion

namespace MehrakCore.Services.Commands.Hsr;

internal static class HsrCommandUtility
{
    public static int GetFloorNumber(string text)
    {
        var startIndex = text.LastIndexOf('(');
        var endIndex = text.LastIndexOf(')');

        if (startIndex == -1 || endIndex == -1 || startIndex >= endIndex) return 0; // Or handle as an error

        string roman = text.Substring(startIndex + 1, endIndex - startIndex - 1).ToUpper();

        if (string.IsNullOrEmpty(roman)) return 0;

        var romanMap = new Dictionary<char, int>
        {
            { 'I', 1 },
            { 'V', 5 },
            { 'X', 10 },
            { 'L', 50 },
            { 'C', 100 },
            { 'D', 500 },
            { 'M', 1000 }
        };

        int total = 0;
        int prevValue = 0;

        for (int i = roman.Length - 1; i >= 0; i--)
        {
            if (!romanMap.TryGetValue(roman[i], out var currentValue)) return 0; // Invalid character

            if (currentValue < prevValue)
                total -= currentValue;
            else
                total += currentValue;
            prevValue = currentValue;
        }

        return total;
    }

    public static string GetRomanNumeral(int number)
    {
        var romanNumerals = new Dictionary<int, string>
        {
            { 1000, "M" },
            { 900, "CM" },
            { 500, "D" },
            { 400, "CD" },
            { 100, "C" },
            { 90, "XC" },
            { 50, "L" },
            { 40, "XL" },
            { 10, "X" },
            { 9, "IX" },
            { 5, "V" },
            { 4, "IV" },
            { 1, "I" }
        };

        var result = new StringBuilder();

        foreach (var kvp in romanNumerals)
            while (number >= kvp.Key)
            {
                result.Append(kvp.Value);
                number -= kvp.Key;
            }

        return result.ToString();
    }
}
