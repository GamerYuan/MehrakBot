#region

using System.Security.Cryptography;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.Application.Services.Common;

public abstract class BaseApplicationService<TContext> : IApplicationService<TContext>
    where TContext : IApplicationContext
{
    private readonly IApiService<GameProfileDto, GameRoleApiContext> m_GameRoleApi;
    private readonly IUserRepository m_UserRepository;
    protected readonly ILogger<BaseApplicationService<TContext>> Logger;

    protected BaseApplicationService(IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        ILogger<BaseApplicationService<TContext>> logger)
    {
        m_GameRoleApi = gameRoleApi;
        m_UserRepository = userRepository;
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

    protected async Task UpdateGameUidAsync(ulong userId, ulong ltuid, Game game, string gameUid, Server server)
    {
        var user = await m_UserRepository.GetUserAsync(userId);
        var profile = user?.Profiles?.FirstOrDefault(p => p.LtUid == ltuid);

        if (user != null && profile != null)
        {
            profile.GameUids.TryAdd(game, []);
            if (profile.GameUids[game].TryAdd(server.ToString(), gameUid))
            {
                await m_UserRepository.CreateOrUpdateUserAsync(user);
            }
        }
    }

    protected async Task UpdateGameUidAsync(ulong userId, ulong ltuid, Game game, string gameUid, string server)
    {
        var user = await m_UserRepository.GetUserAsync(userId);
        var profile = user?.Profiles?.FirstOrDefault(p => p.LtUid == ltuid);

        if (user != null && profile != null)
        {
            profile.GameUids.TryAdd(game, []);
            if (profile.GameUids[game].TryAdd(server.ToString(), gameUid))
            {
                await m_UserRepository.CreateOrUpdateUserAsync(user);
            }
        }
    }
}

public abstract class BaseAttachmentApplicationService<TContext> :
    BaseApplicationService<TContext> where TContext : IApplicationContext
{
    private readonly IAttachmentStorageService m_AttachmentStorageService;

    protected BaseAttachmentApplicationService(
        IApiService<GameProfileDto, GameRoleApiContext> gameRoleApi,
        IUserRepository userRepository,
        IAttachmentStorageService attachmentStorageService,
        ILogger<BaseAttachmentApplicationService<TContext>> logger) : base(gameRoleApi, userRepository, logger)
    {
        m_AttachmentStorageService = attachmentStorageService;
    }

    private static string GetFileName(string serializedData, string gameUid)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{gameUid}_{serializedData}"));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<bool> UploadAttachmentAsync(ulong userId, string storageFileName, Stream fileStream)
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

    private async Task<bool> AttachmentExistsAsync(string storageFileName)
    {
        return await m_AttachmentStorageService.ExistsAsync(storageFileName);
    }
}
