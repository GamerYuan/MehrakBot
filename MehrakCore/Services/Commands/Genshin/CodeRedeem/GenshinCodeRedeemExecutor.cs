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
    public GenshinCodeRedeemExecutor(UserRepository userRepository, TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware, GameRecordApiService gameRecordApi,
        ICodeRedeemRepository codeRedeemRepository,
        ICodeRedeemApiService<GenshinCommandModule> apiService, ILogger<GenshinCodeRedeemExecutor> logger) : base(
        userRepository,
        tokenCacheService, authenticationMiddleware,
        gameRecordApi, apiService, codeRedeemRepository, logger)
    {
    }

    protected override GameName GameName => GameName.Genshin;

    protected override string CommandName => "genshin codes";

    protected override string GetRegionString(Regions server)
    {
        return server switch
        {
            Regions.Asia => "os_asia",
            Regions.Europe => "os_euro",
            Regions.America => "os_usa",
            Regions.Sar => "os_cht",
            _ => throw new ArgumentException("Invalid server name")
        };
    }
}
