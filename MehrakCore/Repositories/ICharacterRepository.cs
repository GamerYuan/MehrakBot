#region

using MehrakCore.Models;

#endregion

namespace MehrakCore.Repositories;

public interface ICharacterRepository
{
    Task<List<string>> GetCharactersAsync(GameName gameName);
    Task<CharacterModel?> GetCharacterModelAsync(GameName gameName);
    Task UpsertCharactersAsync(CharacterModel characterModel);
}
