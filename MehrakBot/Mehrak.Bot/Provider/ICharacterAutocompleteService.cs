#region

#endregion

using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Provider;

public interface ICharacterAutocompleteService<T> where T : ApplicationCommandModule<ApplicationCommandContext>
{
    public IReadOnlyList<string> FindCharacter(string query);
}
