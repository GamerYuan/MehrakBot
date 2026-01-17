#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterCacheService
{
    List<string> GetCharacters(Game gameName);

    Task UpsertCharacters(Game gameName, IEnumerable<string> characters);

    Task DeleteCharacter(Game gameName, string characterName);

    Task UpdateCharactersAsync(Game gameName);

    Task UpdateAllCharactersAsync();

    Dictionary<Game, int> GetCacheStatus();

    void ClearCache();

    void ClearCache(Game gameName);
}
