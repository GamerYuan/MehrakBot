#region

using MehrakCore.Models;

#endregion

namespace MehrakCore.Repositories;

public interface IAliasRepository
{
    Task<Dictionary<string, string>> GetAliasesAsync(GameName gameName);
    Task UpsertCharacterAliasesAsync(AliasModel aliasModel);
}
