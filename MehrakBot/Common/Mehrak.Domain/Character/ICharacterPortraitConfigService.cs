using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character;

public interface ICharacterPortraitConfigService
{
    Task<CharacterPortraitConfig?> GetConfigAsync(Game game, int serverId);
    Task<Dictionary<string, CharacterPortraitConfig>> GetAllConfigsAsync(Game game);
    Task<bool> UpsertConfigAsync(Game game, int serverId, CharacterPortraitConfigUpdate update);
}
