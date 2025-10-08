using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Models;

public class CharacterJsonModel
{
    [JsonPropertyName("game")]
    public required GameName Game { get; init; }
    [JsonPropertyName("characters")]
    public required List<string> Characters { get; init; }

    public GameName GetGameName() => Game;

    public CharacterModel ToCharacterModel() => new CharacterModel
    {
        Game = GetGameName(),
        Characters = Characters
    };
}
