using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Id), IsUnique = true)]
public class UserModel
{
    [Key]
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public List<UserProfileModel> Profiles { get; set; } = [];
}

[Index(nameof(LtUid)), Index(nameof(UserId), nameof(ProfileId), IsUnique = true)]
public class UserProfileModel
{
    public long Id { get; set; }
    public long UserId { get; set; } // FK

    public UserModel User { get; set; } = null!;

    public int ProfileId { get; set; }

    public long LtUid { get; set; }
    public string LToken { get; set; } = string.Empty;
    public DateTime? LastCheckIn { get; set; }

    public List<ProfileGameUid> GameUids { get; set; } = [];
    public List<ProfileRegion> LastUsedRegions { get; set; } = [];
}

public class ProfileGameUid
{
    public long Id { get; set; }
    public long ProfileId { get; set; } // FK
    public UserProfileModel UserProfile { get; set; } = null!;

    public Game Game { get; set; }
    public string Region { get; set; } = string.Empty;
    public string GameUid { get; set; } = string.Empty;
}

public class ProfileRegion
{
    public long Id { get; set; }

    public long ProfileId { get; set; } // FK
    public UserProfileModel UserProfile { get; set; } = null!;

    public Game Game { get; set; }
    public string Region { get; set; } = string.Empty;
}
