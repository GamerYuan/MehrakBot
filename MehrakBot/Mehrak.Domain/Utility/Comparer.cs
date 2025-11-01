#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Utility;

public class CaseInsensitiveCharComparer : IEqualityComparer<char>
{
    public static CaseInsensitiveCharComparer Instance { get; } = new();

    public bool Equals(char x, char y)
    {
        return char.ToLowerInvariant(x) == char.ToLowerInvariant(y);
    }

    public int GetHashCode([DisallowNull] char obj)
    {
        return char.ToLowerInvariant(obj).GetHashCode();
    }
}