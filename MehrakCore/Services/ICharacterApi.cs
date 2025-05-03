#region

using MehrakCore.ApiResponseTypes.Genshin;

#endregion

namespace MehrakCore.Services;

public interface ICharacterApi
{
    Task<IEnumerable<BasicCharacterData>>
        GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    Task<GenshinCharacterInformation?> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid,
        string region,
        uint characterId);
}
