#region

using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Zzz;

public class ZzzCharacterAutocompleteProvider(ICharacterAutocompleteService autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(autocompleteService
            .FindCharacter(Game.ZenlessZoneZero, option.Value ?? string.Empty)
            .Select(x => new ApplicationCommandOptionChoiceProperties(x, x)));
    }
}
