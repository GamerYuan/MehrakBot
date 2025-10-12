using System.Text.RegularExpressions;

namespace Mehrak.Domain.Utility;

public static partial class RegexExpressions
{
    [GeneratedRegex(@",\s*")]
    public static partial Regex RedeemCodeSplitRegex();

    [GeneratedRegex(@"\u2018|\u2019")]
    private static partial Regex QuotationMarkRegex();

    [GeneratedRegex(@"[\s:]")]
    public static partial Regex HsrStatBonusRegex();
}
