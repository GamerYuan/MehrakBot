namespace Mehrak.Bot.Shared.Models;

/// <summary>
/// Structured help metadata emitted by the help source generator. Descriptions and notes are
/// English source strings used as localisation keys; command, parameter and example values stay
/// canonical so help lookups keep working in every language.
/// </summary>
internal sealed class HelpParameterDescriptor
{
    public HelpParameterDescriptor(string name, string description, bool isOptional,
        IReadOnlyList<string> exampleValues)
    {
        Name = name;
        Description = description;
        IsOptional = isOptional;
        ExampleValues = exampleValues;
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsOptional { get; }

    public IReadOnlyList<string> ExampleValues { get; }
}

internal sealed class HelpCommandDescriptor
{
    public HelpCommandDescriptor(
        IReadOnlyList<string> path,
        string description,
        string rootDescription,
        bool isSubcommand,
        string usage,
        IReadOnlyList<string> examples,
        IReadOnlyList<HelpParameterDescriptor> parameters,
        string? notes,
        bool isHidden = false)
    {
        Path = path;
        Description = description;
        RootDescription = rootDescription;
        IsSubcommand = isSubcommand;
        Usage = usage;
        Examples = examples;
        Parameters = parameters;
        Notes = notes;
        IsHidden = isHidden;
    }

    public IReadOnlyList<string> Path { get; }

    public string Description { get; }

    public string RootDescription { get; }

    public bool IsSubcommand { get; }

    public string Usage { get; }

    public IReadOnlyList<string> Examples { get; }

    public IReadOnlyList<HelpParameterDescriptor> Parameters { get; }

    public string? Notes { get; }

    public bool IsHidden { get; }
}
