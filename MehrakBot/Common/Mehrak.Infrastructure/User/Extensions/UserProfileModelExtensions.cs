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
                sb.AppendLine($"- {g.Region.ToFriendlyRegion()}: {g.GameUid}");
            }
        }
        return sb.ToString();
    }

    // ponytail: stub — fill in when more region strings are known
    public static string ToFriendlyRegion(this string region)
    {
        return region switch
        {
            // Genshin
            "os_asia" => "Asia",
            "os_euro" => "Europe",
            "os_usa" => "America",
            "os_cht" => "TW,HK,MO",
            // HSR
            "prod_official_asia" => "Asia",
            "prod_official_eur" => "Europe",
            "prod_official_usa" => "America",
            "prod_official_cht" => "TW,HK,MO",
            // ZZZ
            "prod_gf_jp" => "Asia",
            "prod_gf_eu" => "Europe",
            "prod_gf_us" => "America",
            "prod_gf_sg" => "TW,HK,MO",
            // Hi3
            "overseas01" => "SEA",
            "jp01" => "Japan",
            "kr01" => "Korea",
            "usa01" => "America",
            "asia01" => "Asia",
            "eur01" => "Europe",
            // Fallback
            _ => region
        };
    }
}
