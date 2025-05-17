#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;

#endregion

namespace MehrakCore.Services;

public interface ICharacterApi<T1, T2> where T1 : IBasicCharacterData where T2 : ICharacterDetail
{
    internal Task<IEnumerable<T1>>
        GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    internal Task<ApiResult<T2>> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid,
        string region,
        uint characterId);
}
