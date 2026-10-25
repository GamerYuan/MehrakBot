using System.Globalization;
using System.Reflection;
using Mehrak.Bot.Generated;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Models;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Shared.Services;

/// <summary>
/// NetCord localisations provider backed by the same command UI resources and the generated help
/// descriptors used for <c>/help</c>, so Discord metadata and help text can never drift apart.
/// Command and parameter names stay canonical; only descriptions and enum choice display names
/// are localised.
/// </summary>
internal sealed class CommandUiMetadataLocalizationsProvider : ILocalizationsProvider
{
    /// <summary>Discord's maximum length for command/parameter descriptions.</summary>
    internal const int MaxDescriptionLength = 100;

    /// <summary>Discord's maximum length for option and choice names.</summary>
    internal const int MaxChoiceNameLength = 100;

    private static readonly Lazy<CommandUiMetadataLocalizationsProvider> DefaultInstance =
        new(() => new CommandUiMetadataLocalizationsProvider(CommandUiLocalizer.Default));

    private readonly IReadOnlyList<HelpCommandDescriptor> m_Commands;
    private readonly ICommandUiLocalizer m_Localizer;

    internal CommandUiMetadataLocalizationsProvider(ICommandUiLocalizer localizer,
        IReadOnlyList<HelpCommandDescriptor>? commands = null)
    {
        m_Localizer = localizer;
        m_Commands = commands ?? GeneratedHelpRegistry.Commands;
    }

    /// <summary>Provider instance used when the application command services are configured.</summary>
    internal static CommandUiMetadataLocalizationsProvider Default => DefaultInstance.Value;

    public ValueTask<IReadOnlyDictionary<string, string>?> GetLocalizationsAsync(
        IReadOnlyList<LocalizationPathSegment> path, CancellationToken cancellationToken = default)
    {
        return new ValueTask<IReadOnlyDictionary<string, string>?>(Resolve(path));
    }

    internal IReadOnlyDictionary<string, string>? Resolve(IReadOnlyList<LocalizationPathSegment>? path)
    {
        if (path is null || path.Count == 0) return null;

        if (path[0] is EnumLocalizationPathSegment enumSegment)
            return ResolveEnumField(enumSegment.Type, path);

        // Command and parameter names are intentionally not localised in this release.
        if (path[^1] is not DescriptionLocalizationPathSegment) return null;

        var commandPath = new List<string>();
        string? parameterName = null;

        foreach (var segment in path)
        {
            switch (segment)
            {
                case ApplicationCommandLocalizationPathSegment applicationCommand:
                    commandPath.Add(applicationCommand.Name);
                    break;
                case SubSlashCommandGroupLocalizationPathSegment group:
                    commandPath.Add(group.Name);
                    break;
                case SubSlashCommandLocalizationPathSegment subcommand:
                    commandPath.Add(subcommand.Name);
                    break;
                case SlashCommandParameterLocalizationPathSegment parameter:
                    parameterName = parameter.Name;
                    break;
            }
        }

        if (commandPath.Count == 0) return null;

        var englishText = parameterName is null
            ? FindCommandDescription(commandPath)
            : FindParameterDescription(commandPath, parameterName);

        return englishText is null ? null : TranslateAll(englishText, MaxDescriptionLength);
    }

    private string? FindCommandDescription(IReadOnlyList<string> commandPath)
    {
        var descriptor = Find(commandPath);
        if (descriptor is not null)
            return string.IsNullOrWhiteSpace(descriptor.Description) ? null : descriptor.Description;

        // Only root commands without their own descriptor inherit the toolbox description.
        if (commandPath.Count != 1) return null;

        var root = m_Commands.FirstOrDefault(x =>
            string.Equals(x.Path[0], commandPath[0], StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(root?.RootDescription) ? null : root.RootDescription;
    }

    private string? FindParameterDescription(IReadOnlyList<string> commandPath, string parameterName)
    {
        var descriptor = Find(commandPath);
        var parameter = descriptor?.Parameters.FirstOrDefault(x =>
            string.Equals(x.Name, parameterName, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(parameter?.Description) ? null : parameter!.Description;
    }

    private HelpCommandDescriptor? Find(IReadOnlyList<string> commandPath)
    {
        return m_Commands.FirstOrDefault(x => x.Path.Count == commandPath.Count &&
                                             x.Path.Select((segment, index) =>
                                                     string.Equals(segment, commandPath[index],
                                                         StringComparison.OrdinalIgnoreCase))
                                                 .All(match => match));
    }

    private IReadOnlyDictionary<string, string>? ResolveEnumField(Type? enumType,
        IReadOnlyList<LocalizationPathSegment> path)
    {
        if (enumType is null) return null;

        var field = path.OfType<EnumFieldLocalizationPathSegment>().LastOrDefault()?.Field;
        if (field is null) return null;

        return TranslateAll(GetChoiceLabel(field), MaxChoiceNameLength);
    }

    /// <summary>
    /// Resolves the canonical English display label for an enum field: the
    /// <see cref="SlashCommandChoiceAttribute"/> name when present, otherwise the member name.
    /// </summary>
    private static string GetChoiceLabel(FieldInfo field)
    {
        var choice = field.GetCustomAttribute<SlashCommandChoiceAttribute>();
        return string.IsNullOrWhiteSpace(choice?.Name) ? field.Name : choice!.Name;
    }

    private IReadOnlyDictionary<string, string>? TranslateAll(string englishText, int maxLength)
    {
        Dictionary<string, string>? translations = null;

        foreach (var culture in BotUiCulture.SupportedCultures)
        {
            var token = BotUiCulture.DiscordLocaleToken(culture);
            if (token is null) continue;

            var translated = m_Localizer.Translate(culture, englishText);
            if (string.IsNullOrWhiteSpace(translated)) continue;

            // Never send a value Discord would reject: keep the canonical English default instead.
            if (translated.Length > maxLength) continue;

            translations ??= new Dictionary<string, string>(StringComparer.Ordinal);
            translations[token] = translated;
        }

        return translations is { Count: > 0 } ? translations : null;
    }
}
