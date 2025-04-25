namespace G_BuddyCore.Services;

public interface ICharacterApi
{
    Task<string> GetAllCharactersAsync(ulong uid, string ltoken);
    Task<string> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string characterName);
    Task<string> GetCharacterDataFromIdAsync(ulong uid, string ltoken, uint characterId);
}
