#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models;

internal class CharacterJsonModel
{
    [JsonPropertyName("game")] public required Game Game { get; init; }
    [JsonPropertyName("characters")] public required List<string> Characters { get; init; }
}
