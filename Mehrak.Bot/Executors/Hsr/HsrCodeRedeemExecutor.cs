#region

using Mehrak.Bot.Executors.Executor;
using Mehrak.Bot.Modules;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Bot.Executors.Hsr;

public class HsrCodeRedeemExecutor : BaseCodeRedeemExecutor<HsrCommandModule, HsrCodeRedeemExecutor>
{
    public HsrCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<HsrCommandModule> apiService, ICodeRedeemRepository codeRedeemRepository,
        ILogger<HsrCodeRedeemExecutor> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override GameName GameName => GameName.HonkaiStarRail;

    protected override string CommandName => "hsr codes";

    protected override string GetRegionString(Regions server)
    {
        return server switch
        {
            Regions.Asia => "prod_official_asia",
            Regions.Europe => "prod_official_eur",
            Regions.America => "prod_official_usa",
            Regions.Sar => "prod_official_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
