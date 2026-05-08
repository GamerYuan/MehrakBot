using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Domain.Services.Abstractions;

public interface ICharacterPortraitConfigService
{
    Task<CharacterPortraitConfig?> GetConfigAsync(Game game, string characterName);
    Task<Dictionary<string, CharacterPortraitConfig>> GetAllConfigsAsync(Game game);
    Task UpsertConfigAsync(Game game, string characterName, CharacterPortraitConfigUpdate update);
}
