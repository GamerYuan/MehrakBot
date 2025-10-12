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

namespace MehrakCore.Services.Commands.Hsr.CodeRedeem;

public class HsrCodeRedeemExecutor : BaseCodeRedeemExecutor<HsrCommandModule, HsrCodeRedeemExecutor>
{
    public HsrCodeRedeemExecutor(UserRepository userRepository, RedisCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<HsrCommandModule> apiService, ICodeRedeemRepository codeRedeemRepository,
        ILogger<HsrCodeRedeemExecutor> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override Game Game => Game.HonkaiStarRail;

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
