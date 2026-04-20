using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mehrak.Bot.Generators;

[Generator]
public sealed class HelpTextGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingExampleDescriptor = new(
        "MBOTHELP001",
        "Missing help example value",
        "Parameter '{0}' in command '/{1}' has no [HelpExample] and no [HelpExampleFallback]",
        "HelpGeneration",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modules = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => GetModuleInfo(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var collected = modules.Collect();

        context.RegisterSourceOutput(collected, static (spc, data) => Execute(spc, data));
    }

    private static ModuleInfo? GetModuleInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return null;

        if (FindAttribute(classSymbol.GetAttributes(), "HelpIgnoreAttribute", "HelpIgnore") is not null)
            return null;

        var rootCommandAttribute = FindAttribute(classSymbol.GetAttributes(), "SlashCommandAttribute", "SlashCommand");
        var rootCommandName = GetConstructorStringArgument(rootCommandAttribute, 0);
        var rootCommandDescription = GetConstructorStringArgument(rootCommandAttribute, 1);
        var fallbackExamples = GetFallbackExamples(classSymbol.GetAttributes());
        var diagnostics = new List<HelpDiagnostic>();

        var commands = new List<CommandInfo>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                continue;

            if (FindAttribute(method.GetAttributes(), "HelpIgnoreAttribute", "HelpIgnore") is not null)
                continue;

            var subSlashAttribute = FindAttribute(method.GetAttributes(), "SubSlashCommandAttribute", "SubSlashCommand");
            var slashAttribute = FindAttribute(method.GetAttributes(), "SlashCommandAttribute", "SlashCommand");

            if (subSlashAttribute is null && slashAttribute is null)
                continue;

            var commandName = GetConstructorStringArgument(subSlashAttribute ?? slashAttribute, 0);
            var commandDescription = GetConstructorStringArgument(subSlashAttribute ?? slashAttribute, 1);
            var helpNotes = GetConstructorStringArgument(
                FindAttribute(method.GetAttributes(), "HelpNotesAttribute", "HelpNotes"), 0);

            if (string.IsNullOrWhiteSpace(commandName))
                continue;

            var pathSegments = new List<string>();
            if (subSlashAttribute is not null)
            {
                if (string.IsNullOrWhiteSpace(rootCommandName))
                    continue;

                pathSegments.Add(rootCommandName!);
                pathSegments.Add(commandName!);
            }
            else
            {
                pathSegments.Add(commandName!);
            }

            var parameters = new List<ParameterInfo>();
            foreach (var parameter in method.Parameters)
            {
                var slashParameterAttribute = FindAttribute(parameter.GetAttributes(), "SlashCommandParameterAttribute",
                    "SlashCommandParameter");
                if (slashParameterAttribute is null)
                    continue;

                var parameterName = GetNamedStringArgument(slashParameterAttribute, "Name") ?? parameter.Name;
                var parameterDescription = GetNamedStringArgument(slashParameterAttribute, "Description") ?? string.Empty;
                var explicitExamples = GetHelpExampleValues(
                    FindAttribute(parameter.GetAttributes(), "HelpExampleAttribute", "HelpExample"));
                var explicitExample = explicitExamples.Count > 0 ? explicitExamples[0] : null;

                if (string.IsNullOrWhiteSpace(explicitExample) &&
                    fallbackExamples.TryGetValue(parameterName, out var fallbackExample))
                {
                    explicitExample = fallbackExample;
                    explicitExamples = [fallbackExample];
                }

                if (string.IsNullOrWhiteSpace(explicitExample))
                {
                    var fullPath = string.Join(" ", pathSegments);
                    diagnostics.Add(new HelpDiagnostic(
                        HelpDiagnosticKind.MissingExample,
                        parameter.Locations.FirstOrDefault(),
                        parameterName,
                        fullPath));
                }

                parameters.Add(new ParameterInfo(
                    parameterName,
                    parameterDescription,
                    parameter.HasExplicitDefaultValue,
                    explicitExamples));
            }

            commands.Add(new CommandInfo(
                pathSegments,
                commandDescription ?? string.Empty,
                parameters,
                subSlashAttribute is not null,
                rootCommandDescription ?? string.Empty,
                helpNotes));
        }

        if (commands.Count == 0)
            return null;

        return new ModuleInfo(commands, diagnostics);
    }

    private static Dictionary<string, string> GetFallbackExamples(ImmutableArray<AttributeData> attributes)
    {
        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in attributes)
        {
            if (!IsAttribute(attribute, "HelpExampleFallbackAttribute", "HelpExampleFallback"))
                continue;

            var parameterName = GetConstructorStringArgument(attribute, 0);
            var value = GetConstructorStringArgument(attribute, 1);
            if (parameterName is { Length: > 0 } && value is { Length: > 0 })
                result[parameterName] = value;
        }

        return result;
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<ModuleInfo> modules)
    {
        if (modules.IsDefaultOrEmpty)
            return;

        foreach (var diagnostic in modules.SelectMany(static x => x.Diagnostics))
        {
            switch (diagnostic.Kind)
            {
                case HelpDiagnosticKind.MissingExample:
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingExampleDescriptor,
                        diagnostic.Location,
                        diagnostic.ParameterName,
                        diagnostic.CommandPath));
                    break;
            }
        }

        var allCommands = modules
            .SelectMany(static x => x.Commands)
            .OrderBy(static x => x.Path[0], System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Path.Count)
            .ThenBy(static x => string.Join(" ", x.Path), System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allCommands.Count == 0)
            return;

        var groupedByRoot = allCommands
            .GroupBy(static x => x.Path[0], System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x.Key, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rootOverview = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var subcommandHelp = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
        var rootCommandHelp = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var rootGroup in groupedByRoot)
        {
            var root = rootGroup.Key;
            var subcommands = rootGroup.Where(static x => x.IsSubcommand).OrderBy(static x => x.Path[1], System.StringComparer.OrdinalIgnoreCase).ToList();
            var directCommands = rootGroup.Where(static x => !x.IsSubcommand).ToList();

            if (subcommands.Count > 0)
            {
                rootOverview[root] = BuildOverview(root, subcommands);

                var subMap = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var command in subcommands)
                    subMap[command.Path[1]] = BuildCommandHelp(command);
                subcommandHelp[root] = subMap;
            }
            else
            {
                var command = directCommands[0];
                rootCommandHelp[root] = BuildCommandHelp(command);
            }
        }

        var availableCommands = BuildAvailableCommands(groupedByRoot);

        var source = BuildSource(rootOverview, subcommandHelp, rootCommandHelp, availableCommands);
        context.AddSource("GeneratedHelpRegistry.g.cs", source);
    }

    private static string BuildOverview(string rootCommand, List<CommandInfo> subcommands)
    {
        var description = subcommands.Select(static x => x.RootDescription).FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x));

        var lines = new List<string>
        {
            $"## /{rootCommand}",
            string.IsNullOrWhiteSpace(description) ? "Command help overview." : description,
            "### Subcommands"
        };

        lines.AddRange(subcommands.Select(x => $"- `{x.Path[1]}`: {x.Description}"));
        return string.Join("\n", lines);
    }

    private static string BuildCommandHelp(CommandInfo command)
    {
        var pathText = string.Join(" ", command.Path);
        var lines = new List<string>
        {
            $"## /{pathText}",
            string.IsNullOrWhiteSpace(command.Description) ? "Command help." : command.Description,
            "### Usage",
            $"```{BuildUsage(pathText, command.Parameters)}```"
        };

        if (command.Parameters.Count > 0)
        {
            lines.Add("### Parameters");
            lines.AddRange(command.Parameters.Select(parameter =>
                $"- `{parameter.Name}`: {parameter.Description}{(parameter.IsOptional ? " [Optional]" : string.Empty)}"));
        }

        lines.Add("### Examples");
        lines.Add("```" + string.Join("\n", BuildExamples(pathText, command.Parameters)) + "```");

        if (!string.IsNullOrWhiteSpace(command.HelpNotes))
            lines.Add($"-# {command.HelpNotes}");

        return string.Join("\n", lines);
    }

    private static string BuildUsage(string pathText, List<ParameterInfo> parameters)
    {
        if (parameters.Count == 0)
            return "/" + pathText;

        var parts = new List<string> { "/" + pathText };
        foreach (var parameter in parameters)
        {
            var token = parameter.IsOptional
                ? $"[{parameter.Name}]"
                : $"<{parameter.Name}>";
            parts.Add(token);
        }

        return string.Join(" ", parts);
    }

    private static List<string> BuildExamples(string pathText, List<ParameterInfo> parameters)
    {
        var required = parameters.Where(static p => !p.IsOptional).ToList();
        var optional = parameters.Where(static p => p.IsOptional).ToList();

        var baseTokens = required.Select(static p => $"{p.Name}:{GetPrimaryExampleValue(p)}").ToList();

        var examples = BuildRequiredExamples(pathText, required, baseTokens);
        if (examples.Count == 0)
            examples.Add(BuildCommandLine(pathText, baseTokens));

        var rollingTokens = new List<string>(baseTokens);

        for (var i = 0; i < optional.Count; i++)
        {
            var parameter = optional[i];
            var values = GetAllExampleValues(parameter);

            rollingTokens.Add($"{parameter.Name}:{values[0]}");
            examples.Add(BuildCommandLine(pathText, rollingTokens));

            for (var valueIndex = 1; valueIndex < values.Count; valueIndex++)
            {
                var variantTokens = new List<string>(rollingTokens)
                {
                    [rollingTokens.Count - 1] = $"{parameter.Name}:{values[valueIndex]}"
                };
                examples.Add(BuildCommandLine(pathText, variantTokens));
            }
        }

        return examples;
    }

    private static List<string> BuildRequiredExamples(string pathText, List<ParameterInfo> required, List<string> baseTokens)
    {
        var examples = new List<string>();
        if (required.Count == 0)
        {
            examples.Add(BuildCommandLine(pathText, baseTokens));
            return examples;
        }

        examples.Add(BuildCommandLine(pathText, baseTokens));

        for (var parameterIndex = 0; parameterIndex < required.Count; parameterIndex++)
        {
            var parameter = required[parameterIndex];
            if (parameter.ExampleValues.Count <= 1)
                continue;

            for (var valueIndex = 1; valueIndex < parameter.ExampleValues.Count; valueIndex++)
            {
                var variantTokens = new List<string>(baseTokens)
                {
                    [parameterIndex] = $"{parameter.Name}:{parameter.ExampleValues[valueIndex]}"
                };
                examples.Add(BuildCommandLine(pathText, variantTokens));
            }
        }

        return examples;
    }

    private static string BuildCommandLine(string pathText, List<string> tokens)
    {
        if (tokens.Count == 0)
            return "/" + pathText;

        return "/" + pathText + " " + string.Join(" ", tokens);
    }

    private static string GetPrimaryExampleValue(ParameterInfo parameter)
    {
        if (parameter.ExampleValues.Count > 0)
            return parameter.ExampleValues[0];

        return $"<{parameter.Name}>";
    }

    private static List<string> GetAllExampleValues(ParameterInfo parameter)
    {
        if (parameter.ExampleValues.Count > 0)
            return parameter.ExampleValues;

        return [$"<{parameter.Name}>"];
    }

    private static string BuildAvailableCommands(IReadOnlyCollection<IGrouping<string, CommandInfo>> groups)
    {
        var lines = new List<string> { "Available commands:" };

        foreach (var group in groups)
        {
            var subcommands = group.Where(static x => x.IsSubcommand)
                .Select(static x => x.Path[1])
                .OrderBy(static x => x, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (subcommands.Length > 0)
            {
                lines.Add($"- `/{group.Key}`");
                foreach (var subcommand in subcommands)
                    lines.Add($"  - `{subcommand}`");
            }
            else
            {
                lines.Add($"- `/{group.Key}`");
            }
        }

        lines.Add("Use `/help <command>` or `/help <command> <subcommand>` for details.");
        return string.Join("\n", lines);
    }

    private static string BuildSource(
        Dictionary<string, string> rootOverview,
        Dictionary<string, Dictionary<string, string>> subcommandHelp,
        Dictionary<string, string> rootCommandHelp,
        string availableCommands)
    {
        var builder = new StringBuilder();

        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("namespace Mehrak.Bot.Generated;");
        builder.AppendLine();
        builder.AppendLine("internal static class GeneratedHelpRegistry");
        builder.AppendLine("{");
        builder.AppendLine("    public static string GetAvailableCommandsString() => " + ToLiteral(availableCommands) + ";");
        builder.AppendLine();
        builder.AppendLine("    public static string GetHelpString(string commandName, string subcommand = \"\")");
        builder.AppendLine("    {");
        builder.AppendLine("        if (string.IsNullOrWhiteSpace(commandName))");
        builder.AppendLine("            return GetAvailableCommandsString();");
        builder.AppendLine();
        builder.AppendLine("        commandName = commandName.Trim().TrimStart('/').ToLowerInvariant();");
        builder.AppendLine("        subcommand = subcommand.Trim().ToLowerInvariant();");
        builder.AppendLine();
        builder.AppendLine("        return commandName switch");
        builder.AppendLine("        {");

        var allRoots = rootOverview.Keys
            .Concat(subcommandHelp.Keys)
            .Concat(rootCommandHelp.Keys)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in allRoots)
        {
            if (subcommandHelp.TryGetValue(root, out var subMap) && rootOverview.TryGetValue(root, out var overview))
            {
                builder.AppendLine($"            {ToLiteral(root)} => string.IsNullOrWhiteSpace(subcommand) ? {ToLiteral(overview)} : subcommand switch");
                builder.AppendLine("            {");

                foreach (var sub in subMap.OrderBy(static x => x.Key, System.StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine($"                {ToLiteral(sub.Key)} => {ToLiteral(sub.Value)},");

                builder.AppendLine($"                _ => {ToLiteral(overview)}");
                builder.AppendLine("            },");
            }
            else if (rootCommandHelp.TryGetValue(root, out var rootHelp))
            {
                builder.AppendLine($"            {ToLiteral(root)} => {ToLiteral(rootHelp)},");
            }
        }

        builder.AppendLine("            _ => GetAvailableCommandsString()");
        builder.AppendLine("        };");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName, string shortName)
    {
        if (attribute.AttributeClass is null)
            return false;

        var name = attribute.AttributeClass.Name;
        return name == metadataName || name == shortName;
    }

    private static AttributeData? FindAttribute(ImmutableArray<AttributeData> attributes, params string[] names)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is null)
                continue;

            var name = attribute.AttributeClass.Name;
            foreach (var candidate in names)
            {
                if (name == candidate)
                    return attribute;
            }
        }

        return null;
    }

    private static string? GetConstructorStringArgument(AttributeData? attribute, int index)
    {
        if (attribute is null)
            return null;

        if (attribute.ConstructorArguments.Length <= index)
            return null;

        var value = attribute.ConstructorArguments[index].Value;
        return value as string;
    }

    private static string? GetNamedStringArgument(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key != name)
                continue;

            return named.Value.Value as string;
        }

        return null;
    }

    private static List<string> GetHelpExampleValues(AttributeData? attribute)
    {
        var values = new List<string>();
        if (attribute is null || attribute.ConstructorArguments.Length == 0)
            return values;

        var firstArgument = attribute.ConstructorArguments[0];
        if (firstArgument.Kind == TypedConstantKind.Array)
        {
            foreach (var item in firstArgument.Values)
            {
                if (item.Value is string value && !string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }

        if (firstArgument.Value is string single && !string.IsNullOrWhiteSpace(single))
            values.Add(single);

        return values;
    }

    private static string ToLiteral(string value)
    {
        if (value is null)
            return "null";

        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        return "\"" + escaped + "\"";
    }

    private sealed class ModuleInfo
    {
        public ModuleInfo(List<CommandInfo> commands, List<HelpDiagnostic> diagnostics)
        {
            Commands = commands;
            Diagnostics = diagnostics;
        }

        public List<CommandInfo> Commands { get; }

        public List<HelpDiagnostic> Diagnostics { get; }
    }

    private sealed class CommandInfo
    {
        public CommandInfo(
            List<string> path,
            string description,
            List<ParameterInfo> parameters,
            bool isSubcommand,
            string rootDescription,
            string? helpNotes)
        {
            Path = path;
            Description = description;
            Parameters = parameters;
            IsSubcommand = isSubcommand;
            RootDescription = rootDescription;
            HelpNotes = helpNotes;
        }

        public List<string> Path { get; }

        public string Description { get; }

        public List<ParameterInfo> Parameters { get; }

        public bool IsSubcommand { get; }

        public string RootDescription { get; }

        public string? HelpNotes { get; }
    }

    private sealed class ParameterInfo
    {
        public ParameterInfo(string name, string description, bool isOptional, List<string> exampleValues)
        {
            Name = name;
            Description = description;
            IsOptional = isOptional;
            ExampleValues = exampleValues;
        }

        public string Name { get; }

        public string Description { get; }

        public bool IsOptional { get; }

        public List<string> ExampleValues { get; }
    }

    private sealed class HelpDiagnostic
    {
        public HelpDiagnostic(HelpDiagnosticKind kind, Location? location, string parameterName, string commandPath)
        {
            Kind = kind;
            Location = location;
            ParameterName = parameterName;
            CommandPath = commandPath;
        }

        public HelpDiagnosticKind Kind { get; }

        public Location? Location { get; }

        public string ParameterName { get; }

        public string CommandPath { get; }
    }

    private enum HelpDiagnosticKind
    {
        MissingExample
    }
}
