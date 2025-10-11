using Mehrak.Domain.Models;

namespace Mehrak.Domain.Services.Abstractions;

public interface IApiService<T>
{
    public Task<Result<T>> GetAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");
}

public interface IApiService<T1, T2>
{
    public Task<Result<T1>> GetFirstAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");

    public Task<Result<T2>> GetSecondAsync(ulong ltuid, string ltoken, string gameUid = "", string region = "");
}
