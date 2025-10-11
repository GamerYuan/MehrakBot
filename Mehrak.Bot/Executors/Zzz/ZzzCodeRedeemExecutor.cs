#region

using Mehrak.Bot.Executors.Executor;
using Mehrak.Bot.Modules;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Bot.Executors.Zzz;

public class ZzzCodeRedeemExecutor : BaseCodeRedeemExecutor<ZzzCommandModule, ZzzCodeRedeemExecutor>
{
    public ZzzCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
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

    protected override string GetRegionString(Regions server)
    {
        return server switch
        {
            Regions.Asia => "prod_gf_jp",
            Regions.Europe => "prod_gf_eu",
            Regions.America => "prod_gf_us",
            Regions.Sar => "prod_gf_sg",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
