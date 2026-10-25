using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;

namespace Mehrak.Bot.Shared.Services;

/// <summary>
/// Culture-explicit command UI localiser backed by flat JSON resource files
/// (<c>Resources/CommandUiStrings/{culture}.json</c>, neutral file <c>en.json</c>).
/// Files are loaded once per culture and every lookup is stateless, so concurrent
/// requests for different locales cannot leak into each other.
/// </summary>
internal sealed class CommandUiLocalizer : ICommandUiLocalizer
{
    internal const string ResourceFolderName = "CommandUiStrings";
    internal const string NeutralResourceName = "en";

    private static readonly Lazy<CommandUiLocalizer> DefaultInstance = new(() => new CommandUiLocalizer());

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> m_Resources =
        new(StringComparer.OrdinalIgnoreCase);

    public CommandUiLocalizer(string? resourceDirectory = null)
    {
        ResourceDirectory = resourceDirectory ?? ResolveResourceDirectory();
    }

    /// <summary>Process-wide default store used by static helpers and public overloads.</summary>
    internal static CommandUiLocalizer Default => DefaultInstance.Value;

    internal string ResourceDirectory { get; }

    public string Get(CultureInfo? culture, string key, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(key);

        var normalized = BotUiCulture.Normalize(culture);
        var template = Lookup(BotUiCulture.ResourceName(normalized), key);
        if (template is null)
            return Lookup(NeutralResourceName, CommandUiKeys.ErrorUnexpected) ??
                   "An unexpected error occurred. Please try again later.";

        return Format(normalized, template, arguments);
    }

    public string Translate(CultureInfo? culture, string? englishText)
    {
        if (string.IsNullOrEmpty(englishText)) return englishText ?? string.Empty;

        var normalized = BotUiCulture.Normalize(culture);
        if (normalized.Name == BotUiCulture.English.Name) return englishText;

        return Lookup(BotUiCulture.ResourceName(normalized), englishText) ?? englishText;
    }

    /// <summary>Returns every key shipped for a culture; used by parity validation.</summary>
    internal IReadOnlyDictionary<string, string> Load(CultureInfo? culture) =>
        LoadCore(BotUiCulture.ResourceName(BotUiCulture.Normalize(culture)));

    /// <summary>
    /// Locates the resource directory relative to the application base directory. Content files
    /// are copied to the output and publish directories; both the rooted and flattened layouts
    /// are supported so the provider keeps working if the folder is published without "Resources".
    /// </summary>
    internal static string ResolveResourceDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var nested = Path.Combine(baseDirectory, "Resources", ResourceFolderName);
        if (Directory.Exists(nested)) return nested;

        var flattened = Path.Combine(baseDirectory, ResourceFolderName);
        if (Directory.Exists(flattened)) return flattened;

        return nested;
    }

    private string? Lookup(string resourceName, string key)
    {
        if (resourceName != NeutralResourceName && LoadCore(resourceName).TryGetValue(key, out var translated))
            return translated;

        return LoadCore(NeutralResourceName).TryGetValue(key, out var neutral) ? neutral : null;
    }

    private IReadOnlyDictionary<string, string> LoadCore(string resourceName)
    {
        return m_Resources.GetOrAdd(resourceName, static (name, directory) => ReadFile(directory, name),
            ResourceDirectory);
    }

    private static IReadOnlyDictionary<string, string> ReadFile(string directory, string resourceName)
    {
        var path = Path.Combine(directory, resourceName + ".json");
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            using var stream = File.OpenRead(path);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions);
            return values is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string Format(CultureInfo culture, string template, object[] arguments)
    {
        if (arguments.Length == 0) return template;

        try
        {
            return string.Format(culture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
