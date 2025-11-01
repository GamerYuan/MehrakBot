namespace Mehrak.Domain.Common;

/// <summary>
/// File name format string constants
/// </summary>
public static class FileNameFormat
{
    public static class Genshin
    {
        /// <summary>
        /// Genshin file name format, where {0} is the character ID or other identifier
        /// </summary>
        public const string FileName = "genshin_{0}";

        /// <summary>
        /// Genshin avatar file name format, where {0} is the avatar ID
        /// </summary>
        public const string AvatarName = "genshin_avatar_{0}";

        /// <summary>
        /// Genshin side avatar file name format, where {0} is the avatar ID
        /// </summary>
        public const string SideAvatarName = "genshin_side_avatar_{0}";

        /// <summary>
        /// Genshin skill file name format, where {0} is the character ID and
        /// {1} is the skill ID
        /// </summary>
        public const string SkillName = "genshin_{0}_{1}";

        /// <summary>
        /// Genshin stats file name format, where {0} is the stat ID
        /// </summary>
        public const string StatsName = "genshin_stats_{0}";

        /// <summary>
        /// Genshin buff file name format, where {0} is the buff name (no space)
        /// </summary>
        public const string BuffIconName = "genshin_buff_icon_{0}";
    }

    public static class Hsr
    {
        /// <summary>
        /// HSR file name format, where {0} is the character ID or other identifier
        /// </summary>
        public const string FileName = "hsr_{0}";

        /// <summary>
        /// HSR avatar file name format, where {0} is the avatar ID
        /// </summary>
        public const string AvatarName = "hsr_avatar_{0}";

        /// <summary>
        /// HSR side avatar file name format, where {0} is the avatar ID
        /// </summary>
        public const string SideAvatarName = "hsr_side_avatar_{0}";

        /// <summary>
        /// HSR stats file name format, where {0} is the stat ID
        /// </summary>
        public const string StatsName = "hsr_stats_{0}";

        /// <summary>
        /// HSR weapon icon file name format, where {0} is the weapon ID
        /// </summary>
        public const string WeaponIconName = "hsr_weapon_icon_{0}";

        /// <summary>
        /// HSR end game buff file name format, where {0} is the buff ID
        /// </summary>
        public const string EndGameBuffName = "hsr_endgame_buff_{0}";
    }

    public static class Zzz
    {
        /// <summary>
        /// ZZZ file name format, where {0} is the character ID or other identifier
        /// </summary>
        public const string FileName = "zzz_{0}";

        /// <summary>
        /// ZZZ skill file name format, where {0} is the skill type
        /// </summary>
        public const string SkillName = "zzz_skill_{0}";

        /// <summary>
        /// ZZZ profession file name format, where {0} is the profession ID
        /// </summary>
        public const string ProfessionName = "zzz_profession_{0}";

        /// <summary>
        /// ZZZ avatar file name format, where {0} is the avatar ID
        /// </summary>
        public const string AvatarName = "zzz_avatar_{0}";

        /// <summary>
        /// ZZZ buddy file name format, where {0} is the buddy ID
        /// </summary>
        public const string BuddyName = "zzz_buddy_{0}";

        /// <summary>
        /// ZZZ assault boss name, where {0} is the boss name space removed
        /// </summary>
        public const string AssaultBossName = "zzz_assault_boss_{0}";

        /// <summary>
        /// ZZZ assault buff name, where {0} is the buff name space removed
        /// </summary>
        public const string AssaultBuffName = "zzz_assault_buff_{0}";
    }
}