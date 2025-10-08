#region

using Mehrak.Domain.Services.Abstractions;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;

#endregion

namespace Mehrak.Application.Services;

public interface ICodeRedeemApiService<T> : IApiService<ICodeRedeemExecutor<T>> where T : ICommandModule
{
    public ValueTask<ApiResult<string>> RedeemCodeAsync(string code, string region, string gameUid, ulong ltuid,
        string ltoken);
}
