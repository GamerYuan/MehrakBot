using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.Application.Services.Common;

public abstract class BaseApplicationService<TContext> : IApplicationService<TContext> where TContext : IApplicationContext
{
    private readonly IApiService<GameProfileDto, GameRoleApiContext> m_GameRoleApi;
    protected readonly ILogger<BaseApplicationService<TContext>> Logger;

    protected BaseApplicationService(IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        ILogger<BaseApplicationService<TContext>> logger)
    {
        m_GameRoleApi = gameRoleApi;
        Logger = logger;
    }

    public abstract Task<CommandResult> ExecuteAsync(TContext context);

    protected async Task<GameProfileDto?> GetGameProfileAsync(ulong userId, ulong ltuid, string ltoken, Game game, string region)
    {
        var gameProfileResult = await m_GameRoleApi.GetAsync(new GameRoleApiContext(userId, ltuid, ltoken, game, region));
        if (!gameProfileResult.IsSuccess)
        {
            Logger.LogError("Failed to fetch game profile for User {UserId}, Game {Game}, Region {Region}, Result {@Result}",
                userId, game, region, gameProfileResult);
            return null;
        }

        return gameProfileResult.Data;
    }
}
