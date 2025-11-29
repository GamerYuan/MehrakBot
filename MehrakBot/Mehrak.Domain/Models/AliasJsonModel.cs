#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models;

public class AliasJsonModel
{
    [JsonPropertyName("game")] public Game Game { get; init; }

    [JsonPropertyName("aliases")] public List<AliasEntry> Aliases { get; init; }
}

public class AliasEntry
{
    [JsonPropertyName("name")] public string Name { get; init; }

    [JsonPropertyName("alias")] public string[] Alias { get; init; }
}
