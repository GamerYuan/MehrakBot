using System.Diagnostics.CodeAnalysis;

namespace MehrakCore.Utility;

public class CaseInsensitiveCharComparer : IEqualityComparer<char>
{
    public static CaseInsensitiveCharComparer Instance { get; } = new CaseInsensitiveCharComparer();

    public bool Equals(char x, char y)
    {
        return char.ToLowerInvariant(x) == char.ToLowerInvariant(y);
    }

    public int GetHashCode([DisallowNull] char obj)
    {
        return char.ToLowerInvariant(obj).GetHashCode();
    }
}
