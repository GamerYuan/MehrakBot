#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Infrastructure.Models;

internal class CharacterJson
{
    [JsonPropertyName("game")] public required Game Game { get; init; }
    [JsonPropertyName("characters")] public required List<CharacterJsonModel> Characters { get; init; }
}

internal class CharacterJsonModel
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("baseHp")] public float? BaseHp { get; set; }
    [JsonPropertyName("maxAscHp")] public float? MaxAscHp { get; set; }
}


