using System.Globalization;
using NetCord.Services;

namespace Mehrak.Bot.Shared.Localization;

/// <summary>
/// Maps Discord locale tokens to the fixed set of command UI cultures supported by the bot.
/// Command UI culture is always resolved explicitly from the interaction; no ambient culture
/// is read or mutated, and every unsupported locale falls back to neutral English.
/// </summary>
internal static class BotUiCulture
{
    internal static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Discord locale token to supported culture.</summary>
    private static readonly Dictionary<string, CultureInfo> SupportedLocaleTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de"] = Get("de-DE"),
            ["fr"] = Get("fr-FR"),
            ["ja"] = Get("ja-JP"),
            ["ko"] = Get("ko-KR"),
            ["pt-BR"] = Get("pt-BR"),
            ["zh-CN"] = Get("zh-CN"),
            ["zh-TW"] = Get("zh-TW")
        };

    /// <summary>
    /// Culture names that are accepted as explicit values. There is deliberately no pt-PT
    /// entry: UI text is shipped for pt-BR only and Portuguese (Portugal) falls back to English.
    /// </summary>
    private static readonly Dictionary<string, CultureInfo> SupportedCultureNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["de-DE"] = Get("de-DE"),
            ["fr-FR"] = Get("fr-FR"),
            ["ja-JP"] = Get("ja-JP"),
            ["ko-KR"] = Get("ko-KR"),
            ["pt-BR"] = Get("pt-BR"),
            ["zh-CN"] = Get("zh-CN"),
            ["zh-TW"] = Get("zh-TW")
        };

    /// <summary>Ordered supported cultures including the neutral English fallback.</summary>
    internal static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
    [
        English, Get("zh-CN"), Get("zh-TW"), Get("ja-JP"), Get("ko-KR"), Get("de-DE"), Get("fr-FR"), Get("pt-BR")
    ];

    /// <summary>Discord locale token reported for a supported culture; English has no token.</summary>
    internal static string? DiscordLocaleToken(CultureInfo culture)
    {
        var normalized = Normalize(culture);
        foreach (var pair in SupportedLocaleTokens)
        {
            if (pair.Value.Name == normalized.Name) return pair.Key;
        }

        return null;
    }

    /// <summary>Resolves a Discord locale token to a supported culture, defaulting to English.</summary>
    internal static CultureInfo Resolve(string? discordLocale)
    {
        if (string.IsNullOrWhiteSpace(discordLocale)) return English;

        var token = discordLocale.Trim();
        if (SupportedLocaleTokens.TryGetValue(token, out var fromToken)) return fromToken;
        if (SupportedCultureNames.TryGetValue(token, out var fromName)) return fromName;

        return English;
    }

    /// <summary>Resolves the command UI culture of an interaction, defaulting to English.</summary>
    internal static CultureInfo Resolve(IInteractionContext? context)
    {
        return Resolve(context?.Interaction.UserLocale);
    }

    /// <summary>Normalises any culture to a supported culture, defaulting to English.</summary>
    internal static CultureInfo Normalize(CultureInfo? culture)
    {
        return Resolve(culture?.Name);
    }

    /// <summary>Case-insensitive resource file name for a culture (English is the neutral file).</summary>
    internal static string ResourceName(CultureInfo? culture)
    {
        var normalized = Normalize(culture);
        return normalized.Name == English.Name ? "en" : normalized.Name;
    }

    private static CultureInfo Get(string name) => CultureInfo.GetCultureInfo(name);
}
