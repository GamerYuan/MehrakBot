#region

using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Provider.Autocomplete.Genshin;

public class GenshinCharacterAutocompleteProvider(
    ICharacterAutocompleteService autocompleteService)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        var commaSeparated = option.Value?.Split(',');
        var prefix = string.Join(", ", commaSeparated?[..^1].Select(x => x.Trim()) ?? []);

        prefix = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + ", ";

        var choices = autocompleteService
            .FindCharacter(Domain.Enums.Game.Genshin, commaSeparated?[^1] ?? string.Empty)
            .Select(x =>
            {
                var choice = prefix + x;
                return new ApplicationCommandOptionChoiceProperties(choice, choice);
            });

        return new ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(choices);
    }
}
