using System.Text;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.User.Models;

namespace Mehrak.Infrastructure.User.Extensions;

public static class UserProfileModelExtensions
{
    public static string ToDisplayString(this UserProfileModel profile)
    {
        var sb = new StringBuilder();
        sb.Append($"## Profile {profile.ProfileId}\n**HoYoLAB UID:** {profile.LtUid}\n### Games: \n");
        foreach (var gameUid in profile.GameUids.GroupBy(x => x.Game, (key, g) => new { Key = key, Grouping = g }))
        {
            sb.AppendLine($"**{gameUid.Key.ToFriendlyString()}**");
            foreach (var g in gameUid.Grouping)
            {
                sb.AppendLine($"- {g.Region}: {g.GameUid}");
            }
        }
        return sb.ToString();
    }
}
