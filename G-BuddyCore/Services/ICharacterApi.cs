#region

using G_BuddyCore.ApiResponseTypes.Genshin;

#endregion

namespace G_BuddyCore.Services;

public interface ICharacterApi
{
    Task<IEnumerable<BasicCharacterData>>
        GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    Task<CharacterDetail> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string gameUid, string region,
        string characterName);

    Task<string> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid, string region, uint characterId);
}
