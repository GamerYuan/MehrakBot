namespace MehrakCore.Services.Commands.Hsr;

public class HsrCharacterAutocompleteService
{
    private List<string> CharacterNames { get; }

    public HsrCharacterAutocompleteService()
    {
        CharacterNames =
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Assets", "Hsr", "hsr_character_list.txt"))
                .OrderBy(x => x).ToList();
    }

    public IReadOnlyList<string> FindCharacter(string query, int skip, int limit, out bool more)
    {
        var result = CharacterNames.Where(x => x.Contains(query, StringComparison.InvariantCultureIgnoreCase))
            .Skip(skip);
        List<string> list = new(limit);

        using var enumerator = result.GetEnumerator();
        int i = 0;
        while (true)
            if (enumerator.MoveNext())
            {
                list.Add(enumerator.Current);
                if (++i >= limit)
                {
                    more = enumerator.MoveNext();
                    break;
                }
            }
            else
            {
                more = false;
                break;
            }

        return list;
    }
}
