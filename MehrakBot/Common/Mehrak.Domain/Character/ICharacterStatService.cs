using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character;

public interface ICharacterStatService
{
    Task<(float? BaseVal, float? MaxAscVal)> GetCharAscStatAsync(Game gameName, string characterName);
    Task<Dictionary<string, (float? BaseVal, float? MaxAscVal)>> GetAllCharAscStatsAsync(Game game);
    Task<bool> UpdateCharAscStatAsync(Game game, string characterName, float? baseVal, float? maxAscVal);
}
