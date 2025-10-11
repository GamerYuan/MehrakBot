using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterCacheService
{
    List<string> GetCharacters(Game gameName);

    Dictionary<string, string> GetAliases(Game gameName);

    Task UpdateCharactersAsync(Game gameName);

    Task UpdateAllCharactersAsync();

    Dictionary<Game, int> GetCacheStatus();

    void ClearCache();

    void ClearCache(Game gameName);
}
