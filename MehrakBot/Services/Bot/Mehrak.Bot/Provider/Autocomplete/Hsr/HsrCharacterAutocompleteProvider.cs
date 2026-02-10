#region

using System.Text;
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
        var commaSeparated = option.Value?.Split(',');

        StringBuilder sb = new();

        if (commaSeparated != null)
        {
            for (var i = 0; i < commaSeparated.Length - 1; i++)
            {
                sb.Append(commaSeparated[i]);
                sb.Append(',');
                sb.Append(' ');
            }
        }

        var prefix = sb.ToString();

        var query = commaSeparated?.Length > 0 ? commaSeparated[^1].Trim() : string.Empty;

        var choices = autocompleteService
            .FindCharacter(Domain.Enums.Game.HonkaiStarRail, query)
            .Select(x =>
            {
                var choice = prefix + x;
                return new ApplicationCommandOptionChoiceProperties(choice, choice);
            });

        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(choices);
    }
}
