#region

using System.Security.Cryptography;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Application.Shared.Services;

public abstract class BaseApplicationService : IApplicationService
{
    private readonly IApiService<GameProfileDto, GameRoleApiContext> m_GameRoleApi;
    private readonly UserDbContext m_UserContext;
    protected readonly ILogger<BaseApplicationService> Logger;

    protected virtual string CommandName => "Undefined";
    protected virtual bool RequiresLevel => false;

    protected BaseApplicationService(IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        ILogger<BaseApplicationService> logger)
    {
        m_GameRoleApi = gameRoleApi;
        m_UserContext = userContext;
        Logger = logger;
    }

    public virtual async Task<CommandResult> ExecuteAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteCommandAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CommandResult.Failure(CommandFailureReason.Cancelled, "Command execution cancelled");
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, LogMessage.UnknownError, CommandName, context.UserId, ex.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    protected abstract Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default);

    protected async Task<Result<GameProfileDto>> GetOrFetchGameProfileAsync(ulong userId, ulong ltuid, string ltoken,
        Game game, string region, CancellationToken cancellationToken = default)
    {
        // Convert raw API region string to server enum name for DB storage/lookup
        var serverRegion = GameEnumExtensions.RegionToServerString(game, region);

        // Try DB first for GameUid (never changes per LtUid+Game+Region)
        var cachedProfile = await GetCachedGameProfileAsync(userId, ltuid, game, serverRegion, cancellationToken);

        if (cachedProfile != null)
        {
            var maxLevel = game.GetMaxLevel();
            if (!RequiresLevel || cachedProfile.Level >= maxLevel)
            {
                // Best case: DB hit, no API call needed
                return Result<GameProfileDto>.Success(cachedProfile);
            }

            // Need fresh level — fetch from API (cached in Redis for 10 min anyway)
            var freshResult = await FetchGameProfileAsync(userId, ltuid, ltoken, game, region, cancellationToken);
            if (!freshResult.IsSuccess) return freshResult;

            // Update stored level
            await UpdateStoredLevelAsync(userId, ltuid, game, serverRegion, freshResult.Data.Level, cancellationToken);

            return freshResult;
        }

        // No cached GameUid — full API fetch (happens on first command after profile add, or if DB data was lost)
        var result = await FetchGameProfileAsync(userId, ltuid, ltoken, game, region, cancellationToken);
        if (result.IsSuccess)
        {
            await SaveGameProfileAsync(userId, ltuid, game, serverRegion, result.Data.GameUid, result.Data.Level, cancellationToken);
        }
        return result;
    }

    private async Task<GameProfileDto?> GetCachedGameProfileAsync(ulong userId, ulong ltuid, Game game, string region,
        CancellationToken cancellationToken)
    {
        var entry = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)userId && p.LtUid == (long)ltuid)
            .SelectMany(p => p.GameUids)
            .Where(g => g.Game == game && g.Region == region)
            .Select(g => new { g.GameUid, g.Level })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry == null || string.IsNullOrEmpty(entry.GameUid))
            return null;

        return new GameProfileDto { GameUid = entry.GameUid, Level = entry.Level };
    }

    private async Task<Result<GameProfileDto>> FetchGameProfileAsync(ulong userId, ulong ltuid, string ltoken,
        Game game, string region, CancellationToken cancellationToken)
    {
        var gameProfileResult =
            await m_GameRoleApi.GetAsync(new GameRoleApiContext(userId, ltuid, ltoken, game, region), cancellationToken);
        if (!gameProfileResult.IsSuccess)
        {
            if (gameProfileResult.StatusCode is StatusCode.Cancelled or StatusCode.Timeout)
                return Result<GameProfileDto>.Failure(gameProfileResult.StatusCode, gameProfileResult.ErrorMessage);

            Logger.LogError(
                "Failed to fetch game profile for User {UserId}, Game {Game}, Region {Region}, Result {@Result}",
                userId, game, region, gameProfileResult);
            return Result<GameProfileDto>.Failure(StatusCode.Unauthorized);
        }

        return Result<GameProfileDto>.Success(gameProfileResult.Data);
    }

    private async Task SaveGameProfileAsync(ulong userId, ulong ltuid, Game game, string region, string gameUid,
        int level, CancellationToken cancellationToken)
    {
        var profile = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)userId && p.LtUid == (long)ltuid)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile == null) return;

        m_UserContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = profile.Id,
            Game = game,
            Region = region,
            GameUid = gameUid,
            Level = level
        });

        try
        {
            await m_UserContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e)
        {
            Logger.LogError(e,
                "Failed to save GameUid for User {UserId}, LtUid {LtUid}, Game {Game}, GameUid {GameUid}",
                userId, ltuid, game, gameUid);
        }
    }

    private async Task UpdateStoredLevelAsync(ulong userId, ulong ltuid, Game game, string region, int level,
        CancellationToken cancellationToken)
    {
        await m_UserContext.GameUids
            .Where(g => g.UserProfile.UserId == (long)userId && g.UserProfile.LtUid == (long)ltuid
                        && g.Game == game && g.Region == region)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Level, level), cancellationToken);
    }
}

public abstract class BaseAttachmentApplicationService : BaseApplicationService
{
    private readonly IAttachmentStorageService m_AttachmentStorageService;

    protected virtual string CardName => "Undefined";

    protected BaseAttachmentApplicationService(
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        UserDbContext userContext,
        IAttachmentStorageService attachmentStorageService,
        ILogger<BaseAttachmentApplicationService> logger) : base(gameRoleApi, userContext, logger)
    {
        m_AttachmentStorageService = attachmentStorageService;
    }

    public override async Task<CommandResult> ExecuteAsync(IApplicationContext context, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await ExecuteCommandAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return CommandResult.Failure(CommandFailureReason.Cancelled, "Command execution cancelled");
            }
            catch (OperationCanceledException)
            {
                return CommandResult.Failure(CommandFailureReason.Timeout, ResponseMessage.TimeoutError);
            }
            catch (ImageNotFoundException ex)
            {
                Logger.LogError(ex, "Image not found for User {UserId}, Command {CommandName}: {Message}",
                    context.UserId, CommandName, ex.Message);
                if (attempt == 3)
                {
                    return CommandResult.Failure(CommandFailureReason.BotError, ResponseMessage.ImageUpdateError);
                }
                continue;
            }
            catch (CommandException ex)
            {
                Logger.LogError(ex, "Command error for User {UserId}, Command {CommandName}: {Message}",
                    context.UserId, CommandName, ex.Message);
                return CommandResult.Failure(CommandFailureReason.BotError, ex.Message);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, LogMessage.UnknownError, CommandName, context.UserId, ex.Message);
                return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
            }
        }
        Logger.LogError("Failed to execute command {CommandName} for User {UserId} after 3 attempts", CommandName, context.UserId);
        return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
    }

    protected static string GetFileName(string serializedData, string extension, string gameUid, string? extraData = null)
    {
        var input = extraData != null
            ? $"{gameUid}_{serializedData}_{extraData}"
            : $"{gameUid}_{serializedData}";
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
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
