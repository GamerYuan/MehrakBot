#region

using Mehrak.Bot.Authentication;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Services;

internal abstract class CommandExecutorServiceBase<TContext> : ICommandExecutorService<TContext>
    where TContext : IApplicationContext
{
    public IInteractionContext Context { get; set; } = default!;
    public TContext ApplicationContext { get; set; } = default!;
    public bool ValidateServer { get; set; } = true;
    internal virtual string CommandName { get; set; } = string.Empty;
    internal bool IsResponseEphemeral { get; set; } = false;

    protected readonly List<ParamValidator> Validators = [];

    private readonly IUserRepository m_UserRepository;
    private readonly ICommandRateLimitService m_CommandRateLimitService;
    protected readonly IAuthenticationMiddlewareService AuthenticationMiddleware;
    protected readonly IMetricsService MetricsService;
    protected readonly ILogger<CommandExecutorServiceBase<TContext>> Logger;

    protected CommandExecutorServiceBase(
        IUserRepository userRepository,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        IMetricsService metricsService,
        ILogger<CommandExecutorServiceBase<TContext>> logger)
    {
        m_UserRepository = userRepository;
        m_CommandRateLimitService = commandRateLimitService;
        AuthenticationMiddleware = authenticationMiddleware;
        MetricsService = metricsService;
        Logger = logger;
    }

    public void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null)
    {
        Validators.Add(new ParamValidator<TParam>(paramName, pred, errorMessage));
    }

    public abstract Task ExecuteAsync(uint profile);

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

    protected static string? GetLastUsedServerAsync(UserModel user, Game game, uint profileId)
    {
        var profile = user.Profiles?.FirstOrDefault(x => x.ProfileId == profileId);
        if (profile == null) return null;

        if (profile.LastUsedRegions?.TryGetValue(game, out var server) ?? false) return server;

        return null;
    }

    protected async Task UpdateLastUsedServerAsync(UserModel user, uint profileId, Game game, string server)
    {
        var profile = user.Profiles?.FirstOrDefault(x => x.ProfileId == profileId);
        if (profile == null) return;

        profile.LastUsedRegions ??= [];
        if (profile.LastUsedRegions.TryGetValue(game, out var stored) && stored == server)
            return;

        if (!profile.LastUsedRegions.TryAdd(game, server))
            profile.LastUsedRegions[game] = server;

        await m_UserRepository.CreateOrUpdateUserAsync(user);
    }
}
