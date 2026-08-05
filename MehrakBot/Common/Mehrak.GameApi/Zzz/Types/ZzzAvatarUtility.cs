using System.Text.RegularExpressions;
using Mehrak.Domain.Image.Models;

namespace Mehrak.GameApi.Zzz.Types;

internal static class ZzzAvatarUtility
{
    internal static string GetPortraitImageName(this ZzzAvatarData data)
    {
        return string.Format(FileNameFormat.Zzz.PortraitName, GetOutfitSuffix(data.Id, data.RoleSquareUrl) ?? data.Id.ToString());
    }

    internal static string GetAvatarImageName(int avatarId, string avatarUrl)
    {
        return string.Format(FileNameFormat.Zzz.AvatarName, GetOutfitSuffix(avatarId, avatarUrl) ?? avatarId.ToString());
    }

    /// <summary>
    /// Returns the equipped outfit id when the avatar URL carries a skin suffix
    /// (<c>_{CharacterServerId}_{CharacterOutfitId}</c>), otherwise <see langword="null"/> for the base outfit.
    /// </summary>
    internal static int? GetPortraitSubId(this ZzzAvatarData data)
    {
        var suffix = GetOutfitSuffix(data.Id, data.RoleSquareUrl);
        return suffix != null && int.TryParse(suffix.Split('_')[1], out var subId)
            ? subId
            : null;
    }

    private static string? GetOutfitSuffix(int avatarId, string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl) ||
            !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var hasSkin = Regex.Match(Path.GetFileNameWithoutExtension(uri.LocalPath), $@".*_({avatarId}_\d+)$");
        return hasSkin.Success ? hasSkin.Groups[1].Value : null;
    }
}
