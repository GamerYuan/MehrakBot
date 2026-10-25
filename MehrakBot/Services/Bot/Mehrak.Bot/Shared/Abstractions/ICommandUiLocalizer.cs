using System.Globalization;

namespace Mehrak.Bot.Shared.Abstractions;

/// <summary>
/// Culture-explicit command UI localiser. Every lookup takes the caller's culture so no
/// global or ambient culture state is read or mutated; unsupported cultures and missing
/// translations fall back to neutral English text.
/// </summary>
public interface ICommandUiLocalizer
{
    /// <summary>
    /// Resolves a symbolic UI key (see <c>CommandUiKeys</c>) for the supplied culture and
    /// formats it with <paramref name="arguments"/>. Unknown keys fall back to a safe English error,
    /// never a raw resource key.
    /// </summary>
    string Get(CultureInfo? culture, string key, params object[] arguments);

    /// <summary>
    /// Resolves a translated value for an English source string (Discord metadata, help
    /// descriptions and help notes). Unknown source strings are returned unchanged.
    /// </summary>
    string Translate(CultureInfo? culture, string? englishText);
}
