using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Domain.Repositories;

public interface IAliasRepository
{
    Task<Dictionary<string, string>> GetAliasesAsync(GameName gameName);
    Task UpsertCharacterAliasesAsync(AliasModel aliasModel);
}
