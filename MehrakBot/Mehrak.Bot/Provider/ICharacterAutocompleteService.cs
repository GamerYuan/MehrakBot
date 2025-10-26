#region

#endregion

#region

using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Provider;

public interface ICharacterAutocompleteService<T> where T : ApplicationCommandModule<ApplicationCommandContext>
{
    public IReadOnlyList<string> FindCharacter(string query);
}