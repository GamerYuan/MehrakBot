#region

using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;

#endregion

namespace MehrakCore.Services.Commands;

public interface ICodeRedeemApiService<T> : IApiService<ICodeRedeemExecutor<T>> where T : ICommandModule
{
    public ValueTask<Result<string>> RedeemCodeAsync(string code, string region, string gameUid, ulong ltuid,
        string ltoken);
}
