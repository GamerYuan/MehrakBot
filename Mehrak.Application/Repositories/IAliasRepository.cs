using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;

namespace Mehrak.Application.Repositories;

public interface IAliasRepository
{
    Task<Dictionary<string, string>> GetAliasesAsync(GameName gameName);
    Task UpsertCharacterAliasesAsync(AliasModel aliasModel);
}
