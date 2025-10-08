using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Application.Repositories;

public interface ICharacterRepository
{
    Task<List<string>> GetCharactersAsync(GameName gameName);
    Task<CharacterModel?> GetCharacterModelAsync(GameName gameName);
    Task UpsertCharactersAsync(CharacterModel characterModel);
}
