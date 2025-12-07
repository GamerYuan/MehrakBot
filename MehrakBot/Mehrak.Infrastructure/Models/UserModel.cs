using Mehrak.Domain.Enums;

namespace Mehrak.Infrastructure.Models;

internal class UserModel
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public List<UserProfileModel> Profiles { get; set; } = [];
}

internal class UserProfileModel
{
    public long Id { get; set; }
    public long UserId { get; set; }

    public uint ProfileId { get; set; }
    public long LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;
    public DateTime? LastCheckIn { get; set; }
}

internal class GameUidEntry
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public Game Game { get; set; }
    public string Region { get; set; } = string.Empty;
    public string GameUid { get; set; } = string.Empty;
}

internal class RegionEntry
{
    public long ProfileId { get; set; }
    public Game Game { get; set; }
    public string Region { get; set; } = string.Empty;
}
