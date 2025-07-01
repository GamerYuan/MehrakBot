#region

using MehrakCore.Modules;
using MehrakCore.Services.Commands;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace MehrakCore.Provider.Commands.Hsr;

public class HsrCharacterAutocompleteProvider(ICharacterAutocompleteService<HsrCommandModule> autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(autocompleteService
            .FindCharacter(option.Value!).Select(x => new ApplicationCommandOptionChoiceProperties(x, x)));
    }
}
