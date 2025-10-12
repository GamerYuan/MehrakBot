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

namespace MehrakCore.Services.Commands.Zzz.CodeRedeem;

public class ZzzCodeRedeemExecutor : BaseCodeRedeemExecutor<ZzzCommandModule, ZzzCodeRedeemExecutor>
{
    public ZzzCodeRedeemExecutor(UserRepository userRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<ZzzCommandModule> apiService, ICodeRedeemRepository codeRedeemRepository,
        ILogger<ZzzCodeRedeemExecutor> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override Game Game => Game.ZenlessZoneZero;

    protected override string CommandName => "zzz codes";

    protected override bool UseFollowupForErrors => false;

    protected override string GetRegionString(Server server)
    {
        return server switch
        {
            Server.Asia => "prod_gf_jp",
            Server.Europe => "prod_gf_eu",
            Server.America => "prod_gf_us",
            Server.Sar => "prod_gf_sg",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
