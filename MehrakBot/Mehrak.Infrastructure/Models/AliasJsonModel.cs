#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Infrastructure.Models;

internal class AliasJsonModel
{
    [JsonPropertyName("game")] public required Game Game { get; init; }

    [JsonPropertyName("aliases")] public required List<AliasEntry> Aliases { get; init; }
}

internal class AliasEntry
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("alias")] public required string[] Alias { get; init; }
}
