#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Repositories;

public interface IAliasRepository
{
    Task<Dictionary<string, string>> GetAliasesAsync(Game gameName);

    Task UpsertAliasAsync(Game gameName, Dictionary<string, string> alias);
}
