#region

using Mehrak.Domain.Services.Abstractions;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Common;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Character;

public class GenshinCharacterAutocompleteService : ICharacterAutocompleteService<GenshinCommandModule>
{
    private readonly ICharacterCacheService m_CharacterCacheService;
    private const int Limit = 25;

    public GenshinCharacterAutocompleteService(ICharacterCacheService characterCacheService)
    {
        m_CharacterCacheService = characterCacheService;
    }

    public IReadOnlyList<string> FindCharacter(string query)
    {
        var characterNames = m_CharacterCacheService.GetCharacters(GameName.Genshin);
        return characterNames
            .Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
            .Take(Limit)
            .ToList();
    }
}
