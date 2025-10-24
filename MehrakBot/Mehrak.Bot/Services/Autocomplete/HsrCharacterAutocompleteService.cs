using Mehrak.Bot.Modules;
using Mehrak.Bot.Provider;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Bot.Services.Autocomplete
{
    internal class HsrCharacterAutocompleteService : ICharacterAutocompleteService<HsrCommandModule>
    {
        private readonly ICharacterCacheService m_CharacterCacheService;
        private const int Limit = 25;

        public HsrCharacterAutocompleteService(ICharacterCacheService characterCacheService)
        {
            m_CharacterCacheService = characterCacheService;
        }

        public IReadOnlyList<string> FindCharacter(string query)
        {
            var characterNames = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);
            return [.. characterNames
                .Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
                .Take(Limit)];
        }
    }
}
