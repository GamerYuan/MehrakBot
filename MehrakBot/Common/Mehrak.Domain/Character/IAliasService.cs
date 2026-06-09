using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character;

public interface IAliasService
{
    Dictionary<string, string> GetAliases(Game gameName);
    Task UpsertAliases(Game gameName, Dictionary<string, string> aliases);
    Task DeleteAlias(Game gameName, string alias);
}
