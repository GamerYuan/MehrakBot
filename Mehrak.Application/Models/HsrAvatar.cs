#region

using SixLabors.ImageSharp;

#endregion

namespace Mehrak.Domain.Models;

public sealed record HsrAvatar : IDisposable
{
    public int AvatarId { get; }
    public int Level { get; }
    public int Rarity { get; }
    public int Rank { get; }
    public Image AvatarImage { get; }

    public HsrAvatar(int avatarId, int level, int rarity, int rank, Image avatarImage)
    {
        AvatarId = avatarId;
        Level = level;
        Rarity = rarity;
        Rank = rank;
        AvatarImage = avatarImage;
    }

    public void Dispose()
    {
        AvatarImage.Dispose();
    }

    public bool Equals(HsrAvatar? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return AvatarId == other.AvatarId && Level == other.Level &&
               Rarity == other.Rarity && Rank == other.Rank;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AvatarId, Level, Rarity, Rank);
    }
}

public class HsrAvatarIdComparer : IEqualityComparer<HsrAvatar>, IAlternateEqualityComparer<int, HsrAvatar>
{
    public static HsrAvatarIdComparer Instance { get; } = new();

    public bool Equals(int alternate, HsrAvatar other)
    {
        return alternate.Equals(other.AvatarId);
    }

    public int GetHashCode(int alternate)
    {
        return alternate.GetHashCode();
    }

    public HsrAvatar Create(int alternate)
    {
        throw new NotImplementedException();
    }

    public bool Equals(HsrAvatar? x, HsrAvatar? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.AvatarId == y.AvatarId && x.Level == y.Level && x.Rarity == y.Rarity &&
               x.Rank == y.Rank;
    }

    public int GetHashCode(HsrAvatar obj)
    {
        return obj.AvatarId.GetHashCode();
    }
}
