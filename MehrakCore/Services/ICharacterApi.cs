#region

using MehrakCore.ApiResponseTypes.Genshin;

#endregion

namespace MehrakCore.Services;

public interface ICharacterApi
{
    Task<IEnumerable<BasicCharacterData>>
        GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    Task<CharacterInformation> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string gameUid, string region,
        string characterName);

    Task<CharacterInformation?> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid, string region,
        uint characterId);
}
