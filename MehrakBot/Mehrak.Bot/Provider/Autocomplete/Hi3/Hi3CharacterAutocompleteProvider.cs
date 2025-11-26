using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Provider.Autocomplete.Hi3;

public class Hi3CharacterAutocompleteProvider(
    ICharacterAutocompleteService autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(autocompleteService
            .FindCharacter(Domain.Enums.Game.HonkaiImpact3, option.Value ?? string.Empty)
            .Select(x => new ApplicationCommandOptionChoiceProperties(x, x)));
    }
}
