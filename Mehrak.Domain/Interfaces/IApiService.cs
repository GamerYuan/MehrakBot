using Mehrak.Domain.Models;

namespace Mehrak.Domain.Interfaces;

public interface IApiService<T>
{
    public Task<ApiResult<T>> GetAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");
}

public interface IApiService<T1, T2>
{
    public Task<ApiResult<T1>> GetFirstAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");

    public Task<ApiResult<T2>> GetSecondAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");
}
