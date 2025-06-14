#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;

#endregion

namespace MehrakCore.Services;

public interface ICharacterApi<T1, T2> : IApiService where T1 : IBasicCharacterData where T2 : ICharacterDetail
{
    public Task<IEnumerable<T1>>
        GetAllCharactersAsync(ulong uid, string ltoken, string gameUid, string region);

    public Task<ApiResult<T2>> GetCharacterDataFromIdAsync(ulong uid, string ltoken, string gameUid,
        string region, uint characterId);

    public Task<ApiResult<T2>> GetCharacterDataFromNameAsync(ulong uid, string ltoken, string gameUid, string region,
        string characterName);
}