#region

using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICharacterAutocompleteService<T> where T : ICommandModule
{
    public IReadOnlyList<string> FindCharacter(string query);
}
