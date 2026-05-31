#region

using System.Text.RegularExpressions;

#endregion

namespace Mehrak.Domain.Shared.Utility;

public static partial class RegexExpressions
{
    [GeneratedRegex(@",\s*")]
    public static partial Regex RedeemCodeSplitRegex();

    [GeneratedRegex(@"\u2018|\u2019")]
    public static partial Regex QuotationMarkRegex();

    [GeneratedRegex(@"[\s:]")]
    public static partial Regex HsrStatBonusRegex();
}