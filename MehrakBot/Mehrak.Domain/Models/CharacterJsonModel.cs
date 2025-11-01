#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Models;

public class CharacterJsonModel
{
    [JsonPropertyName("game")] public required Game Game { get; init; }
    [JsonPropertyName("characters")] public required List<string> Characters { get; init; }

    public Game GetGame()
    {
        return Game;
    }

    public CharacterModel ToCharacterModel()
    {
        return new CharacterModel
        {
            Game = GetGame(),
            Characters = Characters
        };
    }
}