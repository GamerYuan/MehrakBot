#region

using MehrakCore.Models;
using MehrakCore.Modules;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICodeRedeemApiService<T> : IApiService where T : ICommandModule
{
    public Task<ApiResult<string>> RedeemCodeAsync(string code, string region, string gameUid, ulong ltuid,
        string ltoken);
}
