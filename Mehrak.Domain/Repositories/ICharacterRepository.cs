using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Domain.Repositories;

public interface ICharacterRepository
{
    Task<List<string>> GetCharactersAsync(Game gameName);
    Task<CharacterModel?> GetCharacterModelAsync(Game gameName);
    Task UpsertCharactersAsync(CharacterModel characterModel);
}
