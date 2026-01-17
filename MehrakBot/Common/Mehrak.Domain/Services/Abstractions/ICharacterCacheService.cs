using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterCacheService
{
    List<string> GetCharacters(Game gameName);

    Task UpsertCharacters(Game gameName, IEnumerable<string> characters);

    Task DeleteCharacter(Game gameName, string characterName);

    Task UpdateAllCharactersAsync();
}
