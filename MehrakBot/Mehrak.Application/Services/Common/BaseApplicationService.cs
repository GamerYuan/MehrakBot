#region

using System.Security.Cryptography;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Common;

public abstract class BaseApplicationService<TContext> : IApplicationService<TContext>
    where TContext : IApplicationContext
{
    private readonly IApiService<GameProfileDto, GameRoleApiContext> m_GameRoleApi;
    private readonly UserDbContext m_UserContext;
    protected readonly ILogger<BaseApplicationService<TContext>> Logger;

    protected BaseApplicationService(IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<BaseApplicationService<TContext>> logger)
    {
        m_GameRoleApi = gameRoleApi;
        m_UserContext = userContext;
        Logger = logger;
    }

    public abstract Task<CommandResult> ExecuteAsync(TContext context);

    protected async Task<GameProfileDto?> GetGameProfileAsync(ulong userId, ulong ltuid, string ltoken, Game game,
        string region)
    {
        var gameProfileResult =
            await m_GameRoleApi.GetAsync(new GameRoleApiContext(userId, ltuid, ltoken, game, region));
        if (!gameProfileResult.IsSuccess)
        {
            Logger.LogError(
                "Failed to fetch game profile for User {UserId}, Game {Game}, Region {Region}, Result {@Result}",
                userId, game, region, gameProfileResult);
            return null;
        }

        return gameProfileResult.Data;
    }

    protected async Task UpdateGameUidAsync(ulong userId, ulong ltuid, Game game, string gameUid, string server)
    {
        var profile = await m_UserContext.UserProfiles
            .AsNoTracking()
            .Where(p => p.UserId == (long)userId && p.LtUid == (long)ltuid)
            .Select(p => new
            {
                p.Id,
                p.ProfileId,
                GameUids = p.GameUids.Where(x => x.Game == game && x.Region == server).ToList()
            })
            .FirstOrDefaultAsync();

        if (profile != null)
        {
            if (profile.GameUids.Count == 0)
            {
                m_UserContext.GameUids.Add(new ProfileGameUid
                {
                    ProfileId = profile.ProfileId,
                    Game = game,
                    GameUid = gameUid,
                    Region = server
                });
            }
            else
            {
                var gameUidEntry = profile.GameUids[0];
                gameUidEntry.GameUid = gameUid;
                gameUidEntry.Region = server;
                m_UserContext.GameUids.Update(gameUidEntry);
            }

            try
            {
                await m_UserContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                Logger.LogError(e,
                    "Failed to update GameUid for User {UserId}, LtUid {LtUid}, Game {Game}, GameUid {GameUid}, Server {Server}",
                    userId, ltuid, game, gameUid, server);
            }
        }
    }
}

public abstract class BaseAttachmentApplicationService<TContext> :
    BaseApplicationService<TContext>, IAttachmentApplicationService<TContext>
    where TContext : IApplicationContext
{
    private readonly IAttachmentStorageService m_AttachmentStorageService;

    protected BaseAttachmentApplicationService(
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<BaseAttachmentApplicationService<TContext>> logger) : base(gameRoleApi, userContext, logger)
    {
        m_AttachmentStorageService = attachmentStorageService;
    }

    protected static string GetFileName(string serializedData, string extension, string gameUid)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{gameUid}_{serializedData}"));
        return $"{Convert.ToHexString(hashBytes).ToLowerInvariant()}.{extension}";
    }

    protected async Task<bool> StoreAttachmentAsync(ulong userId, string storageFileName, Stream fileStream)
    {
        var uploadResult = await m_AttachmentStorageService.StoreAsync(storageFileName, fileStream);
        if (!uploadResult)
        {
            Logger.LogError("Failed to upload attachment for User {UserId}, FileName {FileName}, Result {@Result}",
                userId, storageFileName, uploadResult);
            return false;
        }
        return true;
    }

    protected async Task<bool> AttachmentExistsAsync(string storageFileName)
    {
        return await m_AttachmentStorageService.ExistsAsync(storageFileName);
    }
}
