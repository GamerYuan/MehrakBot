#region

using System.Text.Json.Serialization;

#endregion

namespace MehrakCore.Models;

public class CharacterJsonModel
{
    [JsonPropertyName("game")] public required string Game { get; init; }
    [JsonPropertyName("characters")] public required List<string> Characters { get; init; }

    public GameName GetGameName()
    {
        return Enum.Parse<GameName>(Game, true);
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
