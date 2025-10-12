#region

using Mehrak.Domain.Interfaces;
using Mehrak.Domain.Repositories;
using MehrakCore.Models;
using MehrakCore.Modules;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Genshin.CodeRedeem;

public class GenshinCodeRedeemExecutor : BaseCodeRedeemExecutor<GenshinCommandModule, GenshinCodeRedeemExecutor>
{
    public GenshinCodeRedeemExecutor(UserRepository userRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemRepository codeRedeemRepository,
        ICodeRedeemApiService<GenshinCommandModule> apiService, ILogger<GenshinCodeRedeemExecutor> logger) : base(
        userRepository,
        tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override Game Game => Game.Genshin;

    protected override string CommandName => "genshin codes";

    protected override string GetRegionString(Server server)
    {
        return server switch
        {
            Server.Asia => "os_asia",
            Server.Europe => "os_euro",
            Server.America => "os_usa",
            Server.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
