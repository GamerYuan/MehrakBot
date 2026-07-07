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
        catch (UnauthorizedAccessException)
        {
            return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, LogMessage.UnknownError, CommandName, context.UserId, ex.Message);
            return CommandResult.Failure(CommandFailureReason.Unknown, ResponseMessage.UnknownError);
        }
    }

    protected abstract Task<CommandResult> ExecuteCommandAsync(IApplicationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached GameUid for the given user/game/region from the database, or null if not cached.
    /// GameUid never changes per LtUid+Game+Region so it's safe to cache.
    /// Callers can use this to start the primary API call in parallel with FetchGameProfileAsync.
    /// </summary>
    protected async Task<string?> GetCachedGameUidAsync(ulong userId, ulong ltuid, Game game, string region,
        CancellationToken cancellationToken = default)
    {
        var serverRegion = GameEnumExtensions.RegionToServerString(game, region);

        var entry = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)userId && p.LtUid == (long)ltuid)
            .SelectMany(p => p.GameUids)
            .Where(g => g.Game == game && g.Region == serverRegion)
            .Select(g => g.GameUid)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrEmpty(entry) ? null : entry;
    }

    protected async Task<Result<GameProfileDto>> FetchGameProfileAsync(ulong userId, ulong ltuid, string ltoken,
        Game game, string region, CancellationToken cancellationToken = default)
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

    protected async Task SaveGameUidAsync(ulong userId, ulong ltuid, Game game, string region, string gameUid,
        int level, CancellationToken cancellationToken = default)
    {
        var serverRegion = GameEnumExtensions.RegionToServerString(game, region);

        var profile = await m_UserContext.UserProfiles
            .Where(p => p.UserId == (long)userId && p.LtUid == (long)ltuid)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile == null) return;

        m_UserContext.GameUids.Add(new ProfileGameUid
        {
            ProfileId = profile.Id,
            Game = game,
            Region = serverRegion,
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

    /// <summary>
    /// Fetches the game profile and a primary API result in parallel when a cached GameUid is available.
    /// On first use (no cached GameUid), fetches profile sequentially, saves the GameUid, then fetches primary.
    /// Throws OperationCanceledException on cancelled/timeout, UnauthorizedAccessException on auth failure.
    /// </summary>
    protected async Task<(GameProfileDto Profile, Result<T> Primary)> FetchProfileAndPrimaryAsync<T>(
        ulong userId, ulong ltuid, string ltoken, Game game, string region,
        Func<string, Task<Result<T>>> primaryFetch,
        CancellationToken cancellationToken = default)
    {
        var cachedGameUid = await GetCachedGameUidAsync(userId, ltuid, game, region, cancellationToken);
        var profileTask = FetchGameProfileAsync(userId, ltuid, ltoken, game, region, cancellationToken);

        Task<Result<T>>? primaryTask = null;
        if (cachedGameUid != null)
        {
            primaryTask = primaryFetch(cachedGameUid);
        }

        var profileResult = await profileTask;
        if (!profileResult.IsSuccess)
        {
            if (profileResult.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Cancelled");
            if (profileResult.StatusCode == StatusCode.Timeout)
                throw new OperationCanceledException(profileResult.ErrorMessage ?? "Timeout");
            Logger.LogWarning(LogMessage.InvalidLogin, userId);
            throw new UnauthorizedAccessException(ResponseMessage.AuthError);
        }

        var profile = profileResult.Data;

        if (cachedGameUid == null)
        {
            await SaveGameUidAsync(userId, ltuid, game, region, profile.GameUid, profile.Level, cancellationToken);
            primaryTask = primaryFetch(profile.GameUid);
        }

        var primaryResult = await primaryTask!;
        return (profile, primaryResult);
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
            catch (UnauthorizedAccessException)
            {
                return CommandResult.Failure(CommandFailureReason.AuthError, ResponseMessage.AuthError);
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
