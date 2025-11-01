#region

#endregion

#region

#endregion

using Mehrak.Domain.Enums;

namespace Mehrak.Bot.Provider;

public interface ICharacterAutocompleteService
{
    IReadOnlyList<string> FindCharacter(Game game, string query);
}
