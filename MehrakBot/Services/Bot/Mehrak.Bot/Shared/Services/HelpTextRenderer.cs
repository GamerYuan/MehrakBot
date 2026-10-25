using System.Globalization;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Models;

namespace Mehrak.Bot.Shared.Services;

/// <summary>
/// Renders the help registry for a single interaction culture. Command paths, parameter names and
/// example values stay canonical (English); headings, descriptions and notes are localised.
/// </summary>
internal static class HelpTextRenderer
{
    internal static string Render(
        IReadOnlyList<HelpCommandDescriptor> commands,
        string commandName,
        string subcommand,
        ICommandUiLocalizer localizer,
        CultureInfo? culture)
    {
        commands = commands.Where(x => !x.IsHidden).ToArray();
        if (commands.Count == 0) return string.Empty;

        var root = commandName.Trim().TrimStart('/').ToLowerInvariant();
        var sub = (subcommand ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(root)) return RenderAvailableCommands(commands, localizer, culture);

        var rootCommands = commands
            .Where(x => string.Equals(x.Path[0], root, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rootCommands.Count == 0) return RenderAvailableCommands(commands, localizer, culture);

        var subcommands = rootCommands
            .Where(x => x.IsSubcommand)
            .OrderBy(x => x.Path[1], StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (subcommands.Count == 0) return RenderCommand(rootCommands[0], localizer, culture);

        if (string.IsNullOrEmpty(sub)) return RenderOverview(rootCommands[0].Path[0], subcommands, localizer, culture);

        var match = subcommands.FirstOrDefault(x => string.Equals(x.Path[1], sub, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? RenderOverview(rootCommands[0].Path[0], subcommands, localizer, culture)
            : RenderCommand(match, localizer, culture);
    }

    private static string RenderOverview(string root, IReadOnlyList<HelpCommandDescriptor> subcommands,
        ICommandUiLocalizer localizer, CultureInfo? culture)
    {
        var description = subcommands.Select(x => x.RootDescription)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        List<string> lines =
        [
            $"## /{root}",
            string.IsNullOrWhiteSpace(description)
                ? localizer.Get(culture, CommandUiKeys.HelpOverviewFallback)
                : localizer.Translate(culture, description),
            localizer.Get(culture, CommandUiKeys.HelpSubcommands)
        ];
        lines.AddRange(subcommands.Select(x => localizer.Get(culture, CommandUiKeys.HelpSubcommandLine,
            x.Path[1], localizer.Translate(culture, x.Description))));
        return string.Join("\n", lines);
    }

    private static string RenderCommand(HelpCommandDescriptor command, ICommandUiLocalizer localizer,
        CultureInfo? culture)
    {
        var pathText = string.Join(" ", command.Path);
        List<string> lines =
        [
            $"## /{pathText}",
            string.IsNullOrWhiteSpace(command.Description)
                ? localizer.Get(culture, CommandUiKeys.HelpCommandFallback)
                : localizer.Translate(culture, command.Description),
            localizer.Get(culture, CommandUiKeys.HelpUsage),
            $"```{command.Usage}```"
        ];

        if (command.Parameters.Count > 0)
        {
            lines.Add(localizer.Get(culture, CommandUiKeys.HelpParameters));
            lines.AddRange(command.Parameters.Select(parameter => localizer.Get(culture,
                CommandUiKeys.HelpParameterLine, parameter.Name,
                localizer.Translate(culture, parameter.Description),
                parameter.IsOptional ? " " + localizer.Get(culture, CommandUiKeys.HelpOptional) : string.Empty)));
        }

        lines.Add(localizer.Get(culture, CommandUiKeys.HelpExamples));
        lines.Add("```" + string.Join("\n", command.Examples) + "```");

        if (!string.IsNullOrWhiteSpace(command.Notes))
            lines.Add($"-# {localizer.Translate(culture, command.Notes)}");

        return string.Join("\n", lines);
    }

    private static string RenderAvailableCommands(IReadOnlyList<HelpCommandDescriptor> commands,
        ICommandUiLocalizer localizer, CultureInfo? culture)
    {
        var lines = new List<string> { localizer.Get(culture, CommandUiKeys.HelpAvailableCommands) };

        var groups = commands
            .GroupBy(x => x.Path[0], StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var subcommands = group.Where(x => x.IsSubcommand)
                .Select(x => x.Path[1])
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            lines.Add(localizer.Get(culture, CommandUiKeys.HelpAvailableRoot, group.Key));
            if (subcommands.Length == 0) continue;

            lines.AddRange(subcommands.Select(x => localizer.Get(culture, CommandUiKeys.HelpAvailableSub, x)));
        }

        lines.Add(localizer.Get(culture, CommandUiKeys.HelpUsageHint));
        return string.Join("\n", lines);
    }

    internal static string RenderFooter(string version, ICommandUiLocalizer localizer, CultureInfo? culture)
    {
        return localizer.Get(culture, CommandUiKeys.HelpFooter, version);
    }
}
