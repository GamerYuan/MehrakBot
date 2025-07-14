#region

using System.Text.RegularExpressions;

#endregion

namespace MehrakCore.Models;

public static partial class RegexExpressions
{
    [GeneratedRegex(@",\s*")]
    public static partial Regex RedeemCodeSplitRegex();
}
