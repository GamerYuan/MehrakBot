namespace Mehrak.GameApi.Common.Types;

public class CharacterApiContext : BaseHoYoApiContext
{
    public int CharacterId { get; }

    public CharacterApiContext(ulong userId, ulong ltuid, string lToken, string? gameUid, string? region, int characterId = 0)
        : base(userId, ltuid, lToken, gameUid, region)
    {
        CharacterId = characterId;
    }
}
