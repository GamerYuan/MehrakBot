#region

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Provider.Autocomplete.Hsr;

public class HsrCharacterAutocompleteProvider(ICharacterAutocompleteService autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(autocompleteService
            .FindCharacter(Domain.Enums.Game.HonkaiStarRail, option.Value!).Select(x => new ApplicationCommandOptionChoiceProperties(x, x)));
    }
}
