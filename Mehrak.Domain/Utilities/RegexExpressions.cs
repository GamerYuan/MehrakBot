using System.Text.RegularExpressions;

namespace Mehrak.Domain.Utilities;

public static partial class RegexExpressions
{
    [GeneratedRegex(@",\s*")]
    public static partial Regex RedeemCodeSplitRegex();
}
