using Mehrak.Application.Models.Context;
using Mehrak.Bot.Authentication;
using Mehrak.Bot.Extensions;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

namespace Mehrak.Bot.Services;

public interface ICommandExecutorService<TContext> where TContext : IApplicationContext
{
    Task ExecuteAsync(Server? server, uint profile);

    void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null);
}

internal class CommandExecutorService<TContext> : ICommandExecutorService<TContext> where TContext : IApplicationContext
{
    internal IInteractionContext Context { get; set; } = default!;
    internal TContext ApplicationContext { get; set; } = default!;
    internal string CommandName { get; set; } = string.Empty;
    internal bool IsResponseEphemeral { get; set; } = false;

    private readonly IServiceProvider m_ServiceProvider;
    private readonly IUserRepository m_UserRepository;
    private readonly ICommandRateLimitService m_CommandRateLimitService;
    private readonly IAuthenticationMiddlewareService m_AuthenticationMiddleware;
    private readonly ILogger<CommandExecutorService<TContext>> m_Logger;

    private readonly List<ParamValidator> validators = [];

    public CommandExecutorService(
        IServiceProvider serviceProvider,
        IUserRepository userRepository,
        ICommandRateLimitService commandRateLimitService,
        IAuthenticationMiddlewareService authenticationMiddleware,
        ILogger<CommandExecutorService<TContext>> logger
    )
    {
        m_ServiceProvider = serviceProvider;
        m_UserRepository = userRepository;
        m_CommandRateLimitService = commandRateLimitService;
        m_AuthenticationMiddleware = authenticationMiddleware;
        m_Logger = logger;
    }

    public async Task ExecuteAsync(Server? server, uint profile)
    {
        m_Logger.LogInformation(
            "User {User} used command {Command}",
            Context.Interaction.User.Id, CommandName);

        var invalid = validators.Where(x => !x.IsValid(ApplicationContext)).Select(x => x.ErrorMessage);
        if (invalid.Any())
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2)
                .AddComponents(new TextDisplayProperties(
                    $"Error when validating input:\n{string.Join("\n", invalid)}"))));
            return;
        }

        if (!await ValidateRateLimitAsync()) return;

        var authResult = await m_AuthenticationMiddleware.GetAuthenticationAsync(new(Context, profile));

        if (authResult.IsSuccess)
        {
            server ??= await GetLastUsedServerAsync(profile);
            if (server == null)
            {
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties().WithContent(
                            "Server is required for first time use. Please specify the server parameter.")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            ApplicationContext.LToken = authResult.LToken;
            ApplicationContext.LtUid = authResult.LtUid;

            if (ApplicationContext is ApplicationContextBase context)
            {
                context.Server = server.Value;
            }

            var notesCommandExecutor =
                m_ServiceProvider.GetRequiredService<IApplicationService<TContext>>();
            var commandResult = await notesCommandExecutor
                .ExecuteAsync(ApplicationContext)
                .ConfigureAwait(false);

            if (commandResult.IsSuccess)
            {
                if (!IsResponseEphemeral)
                    await authResult.Context!.Interaction.SendFollowupMessageAsync("Command Execution Completed");
                await authResult.Context.Interaction.SendFollowupMessageAsync(commandResult.Data.ToMessage());
            }
            else
            {
                await authResult.Context!.Interaction.SendFollowupMessageAsync(commandResult.ErrorMessage);
            }
        }
        else if (authResult.Status == AuthStatus.Failure)
        {
            await authResult.Context!.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties().WithContent(authResult.ErrorMessage!)
                    .WithFlags(MessageFlags.Ephemeral)));
        }
    }

    public void AddValidator<TParam>(string paramName, Predicate<TParam> pred, string? errorMessage = null)
    {
        validators.Add(new ParamValidator<TParam>(paramName, pred, errorMessage));
    }

    private async Task<bool> ValidateRateLimitAsync()
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

    private async Task<Server?> GetLastUsedServerAsync(uint profileId)
    {
        var user = await m_UserRepository.GetUserAsync(Context.Interaction.User.Id);
        if (user == null) return null;

        var profile = user.Profiles?.FirstOrDefault(x => x.ProfileId == profileId);
        if (profile == null) return null;

        if (profile.LastUsedRegions?.TryGetValue(Game.Genshin, out var server) ?? false)
        {
            return server;
        }

        return null;
    }
}
