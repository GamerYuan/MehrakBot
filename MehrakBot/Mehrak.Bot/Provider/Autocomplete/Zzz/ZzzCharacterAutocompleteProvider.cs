#region

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Provider.Autocomplete.Hsr;

public class ZzzCharacterAutocompleteProvider(ICharacterAutocompleteService autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(autocompleteService
            .FindCharacter(Domain.Enums.Game.ZenlessZoneZero, option.Value ?? string.Empty)
            .Select(x => new ApplicationCommandOptionChoiceProperties(x, x)));
    }
}
