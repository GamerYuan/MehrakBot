using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Models;

public class AliasJsonModel
{
    [JsonPropertyName("game")]
    public required GameName Game { get; init; }

    [JsonPropertyName("aliases")]
    public required List<AliasEntry> Aliases { get; init; }
}

public class AliasEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("alias")]
    public required string[] Alias { get; init; }
}
