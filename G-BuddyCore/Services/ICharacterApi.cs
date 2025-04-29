namespace G_BuddyCore.Services;

public interface ICharacterApi
{
    Task<string> GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    Task<string> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string gameUid, string region,
        string characterName);

    Task<string> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid, string region, uint characterId);
}
