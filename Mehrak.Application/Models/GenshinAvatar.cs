#region

using SixLabors.ImageSharp;

#endregion

namespace Mehrak.Application.Models;

public sealed record GenshinAvatar : IDisposable
{
    public int AvatarId { get; }
    public int Level { get; }
    public int AvatarType { get; }
    public int Rarity { get; }
    public int Constellation { get; }
    public Image AvatarImage { get; }

    public GenshinAvatar(int avatarId, int level, int rarity, int constellation, Image avatarImage, int avatarType = 1)
    {
        AvatarId = avatarId;
        Level = level;
        Rarity = rarity;
        AvatarType = avatarType;
        Constellation = constellation;
        AvatarImage = avatarImage;
    }

    public void Dispose()
    {
        AvatarImage.Dispose();
    }

    public bool Equals(GenshinAvatar? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return AvatarId == other.AvatarId && Level == other.Level && AvatarType == other.AvatarType &&
               Rarity == other.Rarity && Constellation == other.Constellation;
    }

    public override int GetHashCode()
    {
        return AvatarId.GetHashCode();
    }
}

public class GenshinAvatarIdComparer : IEqualityComparer<GenshinAvatar>, IAlternateEqualityComparer<int, GenshinAvatar>
{
    public static GenshinAvatarIdComparer Instance { get; } = new();

    public bool Equals(int alternate, GenshinAvatar other)
    {
        return alternate.Equals(other.AvatarId);
    }

    public int GetHashCode(int alternate)
    {
        return alternate.GetHashCode();
    }

    public GenshinAvatar Create(int alternate)
    {
        throw new NotImplementedException();
    }

    public bool Equals(GenshinAvatar? x, GenshinAvatar? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null) return false;
        if (y is null) return false;
        if (x.GetType() != y.GetType()) return false;
        return x.AvatarId == y.AvatarId && x.Level == y.Level && x.AvatarType == y.AvatarType && x.Rarity == y.Rarity &&
               x.Constellation == y.Constellation;
    }

    public int GetHashCode(GenshinAvatar obj)
    {
        return obj.AvatarId.GetHashCode();
    }
}
