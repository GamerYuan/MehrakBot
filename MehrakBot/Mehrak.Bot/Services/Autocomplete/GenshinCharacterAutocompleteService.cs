#region

using Mehrak.Bot.Modules;
using Mehrak.Bot.Provider;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;

#endregion

namespace Mehrak.Bot.Services.Autocomplete;

internal class GenshinCharacterAutocompleteService : ICharacterAutocompleteService<GenshinCommandModule>
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private const int Limit = 25;

    public GenshinCharacterAutocompleteService(ICharacterCacheService characterCacheService)
    {
        m_CharacterCacheService = characterCacheService;
    }

    public IReadOnlyList<string> FindCharacter(string query)
    {
        var characterNames = m_CharacterCacheService.GetCharacters(Game.Genshin);
        return
        [
            .. characterNames
                .Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
                .Take(Limit)
        ];
    }
}