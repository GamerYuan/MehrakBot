#region

using Mehrak.Domain.Image.Models;
using Mehrak.GameApi.Genshin.Types;

#endregion

namespace Mehrak.Application.Genshin.Arcana;

internal sealed record GenshinArcanaCardDefinition(int Position, string Name, string AssetKey);

internal sealed record GenshinArcanaResolvedCard(
    int Position,
    string Name,
    string AssetKey,
    bool IsUnlock,
    int UnlockNum);

internal static class GenshinArcanaCardCatalog
{
    private static readonly GenshinArcanaCardDefinition[] CardsInternal =
    [
        Create(1, "The Magician", "magician"),
        Create(2, "The High Priestess", "high_priestess"),
        Create(3, "The Empress", "empress"),
        Create(4, "The Emperor", "emperor"),
        Create(5, "The Hierophant", "hierophant"),
        Create(6, "The Lovers", "lovers"),
        Create(7, "The Chariot", "chariot"),
        Create(8, "Strength", "strength"),
        Create(9, "The Hermit", "hermit"),
        Create(10, "The Wheel of Fortune", "wheel_of_fortune"),
        Create(11, "Justice", "justice"),
        Create(12, "The Hanged Man", "hanged_man"),
        Create(13, "Death", "death"),
        Create(14, "Temperance", "temperance"),
        Create(15, "The Devil", "devil"),
        Create(16, "The Tower", "tower"),
        Create(17, "The Star", "star"),
        Create(18, "The Moon", "moon"),
        Create(19, "The Sun", "sun"),
        Create(20, "Judgment", "judgment"),
        Create(21, "The World", "world"),
        Create(22, "The Fool", "fool")
    ];

    private static readonly Dictionary<string, GenshinArcanaCardDefinition> CardsByName =
        CardsInternal.ToDictionary(x => NormalizeName(x.Name), StringComparer.Ordinal);

    internal static IReadOnlyList<GenshinArcanaCardDefinition> Cards => CardsInternal;

    internal static IReadOnlyList<GenshinArcanaResolvedCard> Resolve(GenshinTarotCardState? state)
    {
        var sourceCards = new GenshinTarotCard?[CardsInternal.Length];

        if (state?.List is { } list)
        {
            foreach (var card in list)
            {
                if (card is null) continue;
                if (!TryGet(card.Name, out var definition)) continue;

                // The first entry for a canonical name wins. Unknown names are ignored so that a
                // payload value can never select a file path or remote URL.
                sourceCards[definition.Position - 1] ??= card;
            }
        }

        var resolved = new GenshinArcanaResolvedCard[CardsInternal.Length];
        for (var index = 0; index < CardsInternal.Length; index++)
        {
            var definition = CardsInternal[index];
            var source = sourceCards[index];
            resolved[index] = new GenshinArcanaResolvedCard(
                definition.Position,
                definition.Name,
                definition.AssetKey,
                source?.IsUnlock ?? false,
                source?.UnlockNum ?? 0);
        }

        return resolved;
    }

    private static bool TryGet(string? name, out GenshinArcanaCardDefinition definition)
    {
        definition = null!;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return CardsByName.TryGetValue(NormalizeName(name), out definition!);
    }

    private static string NormalizeName(string name)
    {
        return string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    private static GenshinArcanaCardDefinition Create(int position, string name, string slug)
    {
        return new GenshinArcanaCardDefinition(position, name,
            string.Format(FileNameFormat.Genshin.ArcanaCardName, slug));
    }
}
