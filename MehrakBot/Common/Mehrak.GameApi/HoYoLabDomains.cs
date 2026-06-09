#region

namespace Mehrak.GameApi;

#endregion

/// <summary>
/// Centralized list of HoYoLAB-related domains. Compose full endpoints in
/// services using these hosts.
/// </summary>
public static class HoYoLabDomains
{
    /// <summary>
    /// Main HoYoLAB public API host used by most endpoints
    /// </summary>
    public const string PublicApi = "https://sg-public-api.hoyolab.com";

    /// <summary>
    /// Genshin Impact Public API host
    /// </summary>
    public const string GenshinApi = "https://sg-hk4e-api.hoyolab.com";

    /// <summary>
    /// Genshin Impact Operation API host
    /// </summary>
    public const string GenshinOpsApi = "https://public-operation-hk4e.hoyolab.com";

    /// <summary>
    /// Honkai: Star Rail Operation API host
    /// </summary>
    public const string HsrOpsApi = "https://public-operation-hkrpg.hoyolab.com";

    /// <summary>
    /// Zenless Zone Zero Operation API host
    /// </summary>
    public const string ZzzOpsApi = "https://public-operation-nap.hoyolab.com";

    /// <summary>
    /// HoYoLAB account API host
    /// </summary>
    public const string AccountApi = "https://api-account-os.hoyolab.com";

    /// <summary>
    /// HoYoLAB Posts/BBS API host
    /// </summary>
    public const string BbsApi = "https://bbs-api-os.hoyolab.com";

    /// <summary>
    /// HoYoWiki static API host
    /// </summary>
    public const string WikiApi = "https://sg-act-public-api-static.hoyolab.com/hoyowiki";

    /// <summary>
    /// HoYoWiki action API host (used for POST queries like get_entry_page_list)
    /// </summary>
    public const string WikiActApi = "https://sg-act-public-api.hoyolab.com/hoyowiki";
}
