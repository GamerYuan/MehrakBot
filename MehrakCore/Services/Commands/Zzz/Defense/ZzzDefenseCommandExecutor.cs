using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Executor;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;

namespace MehrakCore.Services.Commands.Zzz.Defense;

public class ZzzDefenseCommandExecutor : BaseCommandExecutor<ZzzDefenseCommandExecutor>
{
    public ZzzDefenseCommandExecutor(UserRepository userRepository,
        TokenCacheService tokenCacheService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        GameRecordApiService gameRecordApi,
        ILogger<ZzzDefenseCommandExecutor> logger)
        : base(userRepository, tokenCacheService, authenticationMiddleware, gameRecordApi, logger)
    {
    }

    public override ValueTask ExecuteAsync(params object?[] parameters)
    {
        throw new NotImplementedException();
    }

    public override Task OnAuthenticationCompletedAsync(AuthenticationResult result)
    {
        throw new NotImplementedException();
    }
}
