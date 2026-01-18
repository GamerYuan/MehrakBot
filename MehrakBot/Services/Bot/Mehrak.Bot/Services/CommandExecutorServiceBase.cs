#region

using Mehrak.Bot.Authentication;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Extensions;
using Mehrak.Domain.Models;
using Mehrak.Domain.Protobuf;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Infrastructure.Context;
using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Services;

internal abstract class CommandExecutorServiceBase : ICommandExecutorService
{
    public IInteractionContext Context { get; set; } = default!;
    public bool ValidateServer { get; set; } = true;
    internal virtual string CommandName { get; set; } = string.Empty;
    internal bool IsResponseEphemeral { get; set; } = false;
    public Dictionary<string, object> Parameters { get; set; } = [];

    protected readonly List<ParamValidator> Validators = [];
    private readonly UserDbContext m_UserContext;
    private readonly ICommandRateLimitService m_CommandRateLimitService;
    protected readonly IAuthenticationMiddlewareService AuthenticationMiddleware;
    protected readonly IBotMetrics MetricsService;
    protected readonly ILogger<CommandExecutorServiceBase> Logger;
    private readonly ApplicationService.ApplicationServiceClient m_ApplicationClient;

    protected CommandExecutorServiceBase(
        UserDbContext userContext,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IBotMetrics metricsService,
        ApplicationService.ApplicationServiceClient applicationClient,
        ILogger<CommandExecutorServiceBase> logger)
    {
        m_UserContext = userContext;
        m_CommandRateLimitService = commandRateLimitService;
        AuthenticationMiddleware = authenticationMiddleware;
        MetricsService = metricsService;
        m_ApplicationClient = applicationClient;
        Logger = logger;
    }

    public void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null)
    {
        Validators.Add(new ParamValidator<TParam>(paramName, pred, errorMessage));
    }

    public abstract Task ExecuteAsync(int profile);

    protected async Task<Domain.Models.CommandResult> DispatchCommand(
        string commandName,
        ulong discordUserId,
        ulong ltUid,
        string lToken,
        IEnumerable<(string Key, object Value)> parameters,
        CancellationToken cancellationToken = default)
    {
        var request = ProtobufMappingExtensions.ToExecuteRequest(commandName, discordUserId, ltUid, lToken, parameters);
        var response = await m_ApplicationClient.ExecuteCommandAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.ToDomain();
    }

    protected async Task<bool> ValidateRateLimitAsync()
    {
        if (await m_CommandRateLimitService.IsRateLimitedAsync(Context.Interaction.User.Id))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent("Used command too frequent! Please try again later")
                    .WithFlags(MessageFlags.Ephemeral)));
            return false;
        }

        await m_CommandRateLimitService.SetRateLimitAsync(Context.Interaction.User.Id);
        return true;
    }

    protected async Task<string?> GetLastUsedServerAsync(UserDto user, Game game, int profileId)
    {
        var profile = user.Profiles?.FirstOrDefault(x => x.ProfileId == profileId);
        if (profile == null) return null;

        var region = await m_UserContext.Regions
            .AsNoTracking()
            .Where(x => x.ProfileId == profile.Id && x.Game == game)
            .Select(x => new { x.Region })
            .FirstOrDefaultAsync();

        return region?.Region;
    }

    protected async Task UpdateLastUsedServerAsync(UserDto user, int profileId, Game game, string server)
    {
        var profile = user.Profiles?.FirstOrDefault(x => x.ProfileId == profileId);
        if (profile == null) return;

        var region = await m_UserContext.Regions
            .Where(x => x.ProfileId == profile.Id && x.Game == game)
            .FirstOrDefaultAsync();

        try
        {
            if (region == null)
            {
                await m_UserContext.Regions.AddAsync(new ProfileRegion()
                {
                    ProfileId = profile.Id,
                    Game = game,
                    Region = server
                });
            }
            else
            {
                region.Region = server;
                m_UserContext.Update(region);
            }

            await m_UserContext.SaveChangesAsync();
        }
        catch (DbUpdateException e)
        {
            Logger.LogError(e, "Failed to update last used server for user {UserId}, profile {ProfileId}, game {Game}",
                user.Id, profileId, game);
        }
    }

    protected TParam? GetParam<TParam>(string key)
    {
        return Parameters.TryGetValue(key, out var value) && value is TParam tValue ? tValue : default;
    }
}
