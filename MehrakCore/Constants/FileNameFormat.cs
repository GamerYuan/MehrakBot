namespace MehrakCore.Constants;

/// <summary>
/// File name format string constants
/// </summary>
public static class FileNameFormat
{
    /// <summary>
    /// Genshin file name format, where {0} is the character ID or other identifier.
    /// </summary>
    public const string GenshinFileName = "genshin_{0}";

    /// <summary>
    /// Genshin avatar file name format, where {0} is the avatar ID.
    /// </summary>
    public const string GenshinAvatarName = "genshin_avatar_{0}";

    /// <summary>
    /// Genshin side avatar file name format, where {0} is the avatar ID.
    /// </summary>
    public const string GenshinSideAvatarName = "genshin_side_avatar_{0}";

    /// <summary>
    /// Genshin skill file name format, where {0} is the character ID and {1} is the skill ID.
    /// </summary>
    public const string GenshinSkillName = "genshin_{0}_{1}";
}
