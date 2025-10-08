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
    public ZzzCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemApiService<ZzzCommandModule> apiService, ICodeRedeemRepository codeRedeemRepository,
        ILogger<ZzzCodeRedeemExecutor> logger) : base(
        userRepository, tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override GameName GameName => GameName.ZenlessZoneZero;

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
