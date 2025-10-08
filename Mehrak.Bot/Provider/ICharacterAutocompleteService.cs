#region


#region

using MehrakCore.Modules;

#endregion

namespace Mehrak.Domain.Interfaces;

public interface ICharacterAutocompleteService<T> where T : ICommandModule
{
    public IReadOnlyList<string> FindCharacter(string query);
}
