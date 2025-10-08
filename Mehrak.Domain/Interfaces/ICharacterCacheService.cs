using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Interfaces;

public interface ICharacterCacheService
{
    List<string> GetCharacters(GameName gameName);

    Dictionary<string, string> GetAliases(GameName gameName);

    Task UpdateCharactersAsync(GameName gameName);

    Task UpdateAllCharactersAsync();

    Dictionary<GameName, int> GetCacheStatus();

    void ClearCache();

    void ClearCache(GameName gameName);
}
