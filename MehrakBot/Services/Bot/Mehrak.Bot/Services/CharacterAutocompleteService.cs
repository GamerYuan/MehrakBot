#region

using Mehrak.Bot.Provider;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;

#endregion

namespace Mehrak.Bot.Services;

internal class CharacterAutocompleteService : ICharacterAutocompleteService
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private const int Limit = 25;

    public CharacterAutocompleteService(ICharacterCacheService characterCacheService)
    {
        m_CharacterCacheService = characterCacheService;
    }

    public IReadOnlyList<string> FindCharacter(Game game, string query)
    {
        var characterNames = m_CharacterCacheService.GetCharacters(game);
        return
        [
            .. characterNames
                .Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
                .Take(Limit)
        ];
    }
}
