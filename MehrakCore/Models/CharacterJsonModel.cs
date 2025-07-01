#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.Models;

public class CharacterJsonModel
{
    [JsonPropertyName("game")] public required GameName Game { get; init; }
    [JsonPropertyName("characters")] public required List<string> Characters { get; init; }

    public GameName GetGameName()
    {
        return Game;
    }

    public CharacterModel ToCharacterModel()
    {
        return new CharacterModel
        {
            Game = GetGameName(),
            Characters = Characters
        };
    }
}
