using Mehrak.GameApi.Common.Types;

namespace Mehrak.GameApi.Genshin.Types;

public class GenshinCharacterApiContext : CharacterApiContext
{
    public IReadOnlyList<int> CharacterIds { get; }

    public GenshinCharacterApiContext(ulong userId, ulong ltuid, string lToken,
        string? gameUid, string? region, params IEnumerable<int> characterIds)
        : base(userId, ltuid, lToken, gameUid, region)
    {
        CharacterIds = characterIds?.ToList() ?? [];
    }
}
