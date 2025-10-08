#region

using Mehrak.Bot.Modules;
using MehrakCore.Models;
using MehrakCore.Services.Common;

#endregion

namespace Mehrak.Bot.Executors.Hsr;

public class HsrCharacterAutocompleteService : ICharacterAutocompleteService<HsrCommandModule>
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private const int Limit = 25;

    public HsrCharacterAutocompleteService(ICharacterCacheService characterCacheService)
    {
        m_CharacterCacheService = characterCacheService;
    }

    public IReadOnlyList<string> FindCharacter(string query)
    {
        var characterNames = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);
        return characterNames
            .Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
            .Take(Limit)
            .ToList();
    }
}
