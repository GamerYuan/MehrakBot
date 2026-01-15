#region

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mehrak.Bot.Modules.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

#endregion

namespace Mehrak.Bot.Authentication;

public class AuthenticationMiddlewareService : IAuthenticationMiddlewareService
{
    private readonly ICacheService m_CacheService;
    private readonly IEncryptionService m_EncryptionService;
    private readonly IServiceScopeFactory m_ServiceScopeFactory;
    private readonly ILogger<AuthenticationMiddlewareService> m_Logger;
    private readonly ConcurrentDictionary<string, AuthenticationResponse> m_NotifiedRequests = [];
    private readonly ConcurrentDictionary<string, byte> m_CurrentRequests = [];

    private float TimeoutMinutes { get; set; } = 1;

    public AuthenticationMiddlewareService(
        ICacheService cacheService,
        IEncryptionService encryptionService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AuthenticationMiddlewareService> logger)
    {
        m_CacheService = cacheService;
        m_EncryptionService = encryptionService;
        m_ServiceScopeFactory = serviceScopeFactory;
        m_Logger = logger;
    }

    public async Task<AuthenticationResult> GetAuthenticationAsync(AuthenticationRequest request)
    {
        m_Logger.LogDebug("GetAuthenticationAsync started for UserId={UserId}, ProfileId={ProfileId}",
            request.Context.Interaction.User.Id, request.ProfileId);

        using var scope = m_ServiceScopeFactory.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<UserDbContext>();

        var user = await userContext.Users
            .AsNoTracking()
            .Where(u => u.Id == (long)request.Context.Interaction.User.Id)
            .Select(u => new UserDto()
            {
                Id = (ulong)u.Id,
                Profiles = u.Profiles.Where(x => x.ProfileId == request.ProfileId).Select(p => new UserProfileDto()
                {
                    Id = p.Id,
                    ProfileId = p.ProfileId,
                    LtUid = (ulong)p.LtUid,
                    LToken = p.LToken
                }).ToList()
            }).FirstOrDefaultAsync();

        if (user == null)
        {
            m_Logger.LogWarning("User account not found for UserId={UserId}", request.Context.Interaction.User.Id);
            return AuthenticationResult.NotFound(request.Context, "User account not found. Please add a profile first.");
        }

        var profile = user.Profiles?.FirstOrDefault();

        if (profile == null)
        {
            m_Logger.LogWarning("Profile not found for UserId={UserId}, ProfileId={ProfileId}",
                request.Context.Interaction.User.Id, request.ProfileId);
            return AuthenticationResult.NotFound(request.Context, "No profiles found. Please add a profile first.");
        }

        var cacheKey = CacheKeys.BotLToken(request.Context.Interaction.User.Id, profile.LtUid);
        m_Logger.LogDebug("Checking cache for LToken. UserId={UserId}, LtUid={LtUid}",
            request.Context.Interaction.User.Id, profile.LtUid);
        var token = await m_CacheService.GetAsync<string>(cacheKey);

        if (token != null)
        {
            m_Logger.LogDebug("Cache hit for LToken. UserId={UserId}, LtUid={LtUid}",
                request.Context.Interaction.User.Id, profile.LtUid);
            await request.Context.Interaction.SendResponseAsync(
                InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
            return AuthenticationResult.Success(request.Context.Interaction.User.Id, profile.LtUid, token, user,
                request.Context);
        }

        var guid = Guid.NewGuid().ToString();
        m_CurrentRequests.TryAdd(guid, 1);
        await request.Context.Interaction.SendResponseAsync(InteractionCallback.Modal(AuthModalModule.AuthModal(guid)));
        m_Logger.LogDebug("Auth modal sent. Guid={Guid}, UserId={UserId}, LtUid={LtUid}", guid,
            request.Context.Interaction.User.Id, profile.LtUid);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMinutes(TimeoutMinutes));
        m_Logger.LogDebug("Waiting for authentication response. Guid={Guid}, TimeoutMinutes={TimeoutMinutes}", guid,
            TimeoutMinutes);
        var authResponse = await WaitForAuthenticationAsync(guid, cts.Token).ConfigureAwait(false);

        if (authResponse is null)
        {
            m_Logger.LogInformation("Authentication timed out. Guid={Guid}, UserId={UserId}", guid,
                request.Context.Interaction.User.Id);
            return AuthenticationResult.Timeout();
        }

        await authResponse.Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            token = m_EncryptionService.Decrypt(profile.LToken, authResponse.Passphrase);
            m_Logger.LogDebug("Cookie decryption succeeded. Guid={Guid}, UserId={UserId}, LtUid={LtUid}",
                authResponse.Guid, request.Context.Interaction.User.Id, profile.LtUid);
        }
        catch (AuthenticationTagMismatchException e)
        {
            m_Logger.LogWarning(e, "Incorrect passphrase provided. Guid={Guid}, UserId={UserId}", authResponse.Guid,
                request.Context.Interaction.User.Id);
            return AuthenticationResult.Failure(authResponse.Context, "Incorrect passphrase. Please try again");
        }

        await m_CacheService.SetAsync(new CacheEntryBase<string>(cacheKey, token, TimeSpan.FromMinutes(10)));
        m_Logger.LogDebug("Authentication succeeded. UserId={UserId}, LtUid={LtUid}",
            request.Context.Interaction.User.Id, profile.LtUid);
        return AuthenticationResult.Success(request.Context.Interaction.User.Id, profile.LtUid, token, user,
            authResponse.Context);
    }

    public bool NotifyAuthenticate(AuthenticationResponse request)
    {
        if (!m_CurrentRequests.TryGetValue(request.Guid, out _))
        {
            m_Logger.LogWarning("No authentication requests found. Guid={Guid}", request.Guid);
            return false;
        }

        m_Logger.LogDebug("NotifyAuthenticateAsync received. Guid={Guid}, UserId={UserId}", request.Guid,
            request.UserId);
        return m_NotifiedRequests.TryAdd(request.Guid, request);
    }

    private async Task<AuthenticationResponse?> WaitForAuthenticationAsync(string guid, CancellationToken token)
    {
        try
        {
            m_Logger.LogDebug("WaitForAuthenticationAsync started. Guid={Guid}", guid);
            AuthenticationResponse? response;
            while (!m_NotifiedRequests.TryRemove(guid, out response))
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(100, token);
            }

            m_CurrentRequests.TryRemove(guid, out _);
            m_Logger.LogDebug("Authentication response dequeued. Guid={Guid}", guid);
            return response;
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogDebug("WaitForAuthenticationAsync canceled. Guid={Guid}", guid);
            return null;
        }
    }
}
