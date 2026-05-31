using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character;

public record struct CharacterUpsertEntry(string Name, int? ServerId = null);

public interface ICharacterCacheService
{
    List<string> GetCharacters(Game gameName);

    Task UpsertCharacters(Game gameName, IEnumerable<string> characters);

    Task UpsertCharacters(Game gameName, IEnumerable<CharacterUpsertEntry> entries);

    Task DeleteCharacter(Game gameName, string characterName);

    Task UpdateAllCharactersAsync();
}
