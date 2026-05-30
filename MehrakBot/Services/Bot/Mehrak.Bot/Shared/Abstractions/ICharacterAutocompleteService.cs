#region

#endregion

#region

#endregion

using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Bot.Shared.Abstractions;

public interface ICharacterAutocompleteService
{
    IReadOnlyList<string> FindCharacter(Game game, string query);
}
