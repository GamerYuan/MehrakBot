using SixLabors.ImageSharp;

namespace Mehrak.Domain.Models;

public sealed record ZzzAvatar : IDisposable
{
    public int AvatarId { get; }
    public int Level { get; }
    public char Rarity { get; }
    public int Rank { get; }
    public Image AvatarImage { get; }

    public ZzzAvatar(int avatarId, int level, char rarity, int rank, Image avatarImage)
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

    public bool Equals(ZzzAvatar? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return AvatarId == other.AvatarId && Level == other.Level &&
               Rarity == other.Rarity && Rank == other.Rank;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AvatarId, Level, Rarity, Rank);
    }
}

public class ZzzAvatarIdComparer : IEqualityComparer<ZzzAvatar>, IAlternateEqualityComparer<int, ZzzAvatar>
{
    public static ZzzAvatarIdComparer Instance { get; } = new();

    public bool Equals(int alternate, ZzzAvatar other)
    {
        return alternate.Equals(other.AvatarId);
    }

    public int GetHashCode(int alternate)
    {
        return alternate.GetHashCode();
    }

    public ZzzAvatar Create(int alternate)
    {
        throw new NotImplementedException();
    }

    public bool Equals(ZzzAvatar? x, ZzzAvatar? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.AvatarId == y.AvatarId && x.Level == y.Level && x.Rarity == y.Rarity &&
               x.Rank == y.Rank;
    }

    public int GetHashCode(ZzzAvatar obj)
    {
        return obj.AvatarId.GetHashCode();
    }
}
