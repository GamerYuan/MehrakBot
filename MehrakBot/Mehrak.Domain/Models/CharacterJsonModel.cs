using Mehrak.Domain.Enums;
using System.Text.Json.Serialization;

namespace Mehrak.Domain.Models;

public class CharacterJsonModel
{
    [JsonPropertyName("game")]
    public required Game Game { get; init; }
    [JsonPropertyName("characters")]
    public required List<string> Characters { get; init; }

    public Game GetGame() => Game;

    public CharacterModel ToCharacterModel() => new()
    {
        Game = GetGame(),
        Characters = Characters
    };
}
