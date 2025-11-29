#region

using Mehrak.Bot.Services;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Builders;

public interface ICommandExecutorBuilder
{
    ICommandExecutorBuilder<TContext> For<TContext>() where TContext : IApplicationContext;
}

public interface ICommandExecutorBuilder<TContext> where TContext : IApplicationContext
{
    ICommandExecutorBuilder<TContext> WithInteractionContext(IInteractionContext context);

    ICommandExecutorBuilder<TContext> WithApplicationContext(TContext appContext);

    ICommandExecutorBuilder<TContext> WithCommandName(string commandName);

    ICommandExecutorBuilder<TContext> WithEphemeralResponse(bool ephemeral = true);

    ICommandExecutorBuilder<TContext> AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null);

    ICommandExecutorBuilder<TContext> ValidateServer(bool validate);

    ICommandExecutorService<TContext> Build();
}

internal class CommandExecutorBuilder : ICommandExecutorBuilder
{
    private readonly IServiceProvider m_ServiceProvider;

    public CommandExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public ICommandExecutorBuilder<TContext> For<TContext>() where TContext : IApplicationContext
    {
        return ActivatorUtilities.CreateInstance<CommandExecutorBuilder<TContext>>(m_ServiceProvider);
    }
}

internal class CommandExecutorBuilder<TContext> : ICommandExecutorBuilder<TContext>
    where TContext : IApplicationContext
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly List<Action<CommandExecutorService<TContext>>> m_Configurators = [];

    private IInteractionContext? m_InteractionContext;
    private TContext? m_AppContext;
    private string? m_CommandName;
    private bool m_Ephemeral;

    public CommandExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public ICommandExecutorBuilder<TContext> WithInteractionContext(IInteractionContext context)
    {
        m_InteractionContext = context ?? throw new ArgumentNullException(nameof(context));
        return this;
    }

    public ICommandExecutorBuilder<TContext> WithApplicationContext(TContext appContext)
    {
        m_AppContext = appContext ?? throw new ArgumentNullException(nameof(appContext));
        return this;
    }

    public ICommandExecutorBuilder<TContext> WithCommandName(string commandName)
    {
        m_CommandName = commandName;
        return this;
    }

    public ICommandExecutorBuilder<TContext> WithEphemeralResponse(bool ephemeral = true)
    {
        m_Ephemeral = ephemeral;
        return this;
    }

    public ICommandExecutorBuilder<TContext> AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            throw new ArgumentException("Parameter name cannot be null or whitespace.", nameof(paramName));
        if (predicate is null)
            throw new ArgumentNullException(nameof(predicate));

        m_Configurators.Add(svc => svc.AddValidator(paramName, predicate, errorMessage));
        return this;
    }

    public ICommandExecutorBuilder<TContext> ValidateServer(bool validate)
    {
        m_Configurators.Add(svc => svc.ValidateServer = validate);
        return this;
    }

    public ICommandExecutorService<TContext> Build()
    {
        if (m_InteractionContext is null)
            throw new InvalidOperationException("Interaction context must be provided.");
        if (m_AppContext is null)
            throw new InvalidOperationException("Application context must be provided.");

        CommandExecutorService<TContext> executor = ActivatorUtilities.CreateInstance<CommandExecutorService<TContext>>(m_ServiceProvider);

        executor.Context = m_InteractionContext;
        executor.ApplicationContext = m_AppContext;
        executor.CommandName = m_CommandName ?? string.Empty;
        executor.IsResponseEphemeral = m_Ephemeral;

        foreach (Action<CommandExecutorService<TContext>> configure in m_Configurators)
            configure(executor);

        return executor;
    }
}

public static class CommandExecutorBuilderServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecutorBuilder(this IServiceCollection services)
    {
        services.AddTransient<ICommandExecutorBuilder, CommandExecutorBuilder>();
        services.AddTransient(typeof(ICommandExecutorBuilder<>), typeof(CommandExecutorBuilder<>));
        return services;
    }
}
