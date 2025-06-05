namespace MehrakCore.Services.Commands.Hsr;

public class HsrCharacterAutocompleteService
{
    private List<string> CharacterNames { get; }

    private const int Limit = 25;

    public HsrCharacterAutocompleteService()
    {
        CharacterNames =
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "hsr_character_list.txt"))
                .OrderBy(x => x).ToList();
    }

    public IReadOnlyList<string> FindCharacter(string query)
    {
        return CharacterNames.Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase)).Take(Limit)
            .ToList();
    }
}
