#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models;

public class UserDto
{
    public ulong Id { get; set; }
    public DateTime Timestamp { get; set; }
    public IEnumerable<UserProfileDto>? Profiles { get; set; } = null;
}

public class UserProfileDto
{
    public long Id { get; set; }

    public int ProfileId { get; set; }

    public ulong LtUid { get; set; }

    public string LToken { get; set; } = string.Empty;

    public DateTime? LastCheckIn { get; set; }

    public Dictionary<Game, Dictionary<string, string>> GameUids { get; set; } = [];

    public Dictionary<Game, string> LastUsedRegions { get; set; } = [];
}
