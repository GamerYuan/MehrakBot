#region

using Mehrak.Domain.Enums;

#endregion

namespace Mehrak.Domain.Repositories;

public interface ICharacterRepository
{
    Task<List<string>> GetCharactersAsync(Game gameName);

    Task UpsertCharactersAsync(Game gameName, IEnumerable<string> characters);
}
