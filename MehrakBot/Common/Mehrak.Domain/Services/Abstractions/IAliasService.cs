using Mehrak.Domain.Enums;

namespace Mehrak.Domain.Services.Abstractions;

public interface IAliasService
{
    Dictionary<string, string> GetAliases(Game gameName);
    Task UpsertAliases(Game gameName, Dictionary<string, string> aliases);
    Task DeleteAlias(Game gameName, string alias);
    Task UpdateAllAliasesAsync();
}
