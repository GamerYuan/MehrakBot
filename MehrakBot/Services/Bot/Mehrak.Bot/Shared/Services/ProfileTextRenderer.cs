using System.Globalization;
using System.Text;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.User.Models;

namespace Mehrak.Bot.Shared.Services;

/// <summary>Localises Bot profile UI without changing the shared English profile representation.</summary>
internal static class ProfileTextRenderer
{
    internal static string Render(UserProfileModel profile, ICommandUiLocalizer localizer, CultureInfo culture)
    {
        var builder = new StringBuilder(localizer.Get(culture, CommandUiKeys.ProfileDisplayHeader,
            profile.ProfileId, profile.LtUid));
        foreach (var game in profile.GameUids.GroupBy(x => x.Game))
        {
            // Game names, region identifiers and UIDs are data, not command UI.
            builder.AppendLine($"**{game.Key.ToFriendlyString()}**");
            foreach (var uid in game)
                builder.AppendLine($"- {uid.Region}: {uid.GameUid}");
        }
        return builder.ToString();
    }
}
