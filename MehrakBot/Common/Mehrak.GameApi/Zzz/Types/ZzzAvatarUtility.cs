using System.Text.RegularExpressions;
using Mehrak.Domain.Common;

namespace Mehrak.GameApi.Zzz.Types;

internal static class ZzzAvatarUtility
{
    internal static string GetAvatarImageName(int avatarId, string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl) ||
            !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return string.Format(FileNameFormat.Zzz.AvatarName, avatarId);
        }

        var hasSkin = Regex.Match(Path.GetFileNameWithoutExtension(uri.LocalPath), $@".*_({avatarId}_\d+)$");
        if (hasSkin.Success)
            return string.Format(FileNameFormat.Zzz.AvatarName, hasSkin.Groups[1].Value);
        else
            return string.Format(FileNameFormat.Zzz.AvatarName, avatarId);
    }
}
