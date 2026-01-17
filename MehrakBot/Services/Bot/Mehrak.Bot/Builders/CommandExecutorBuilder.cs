#region

using Mehrak.Bot.Services;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;

#endregion

namespace Mehrak.Bot.Builders;

public interface ICommandExecutorBuilder
{
    ICommandExecutorBuilder WithInteractionContext(IInteractionContext context);

    ICommandExecutorBuilder AddParameters<TParam>(string key, TParam value);

    ICommandExecutorBuilder WithCommandName(string commandName);

    ICommandExecutorBuilder WithEphemeralResponse(bool ephemeral = true);

    ICommandExecutorBuilder AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null);

    ICommandExecutorBuilder WithParameters(params IEnumerable<(string Key, object Value)> parameters);

    ICommandExecutorBuilder ValidateServer(bool validate);

    ICommandExecutorService Build();
}

internal class CommandExecutorBuilder : ICommandExecutorBuilder
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly List<Action<CommandExecutorService>> m_Configurators = [];
    private readonly Dictionary<string, object> m_Params = [];

    private IInteractionContext? m_InteractionContext;
    private string? m_CommandName;
    private bool m_Ephemeral;

    public CommandExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public ICommandExecutorBuilder WithInteractionContext(IInteractionContext context)
    {
        m_InteractionContext = context ?? throw new ArgumentNullException(nameof(context));
        return this;
    }

    public ICommandExecutorBuilder AddParameters<TParam>(string key, TParam value)
    {
        ArgumentNullException.ThrowIfNull(value);
        m_Params.Add(key, value);
        return this;
    }

    public ICommandExecutorBuilder WithCommandName(string commandName)
    {
        m_CommandName = commandName;
        return this;
    }

    public ICommandExecutorBuilder WithEphemeralResponse(bool ephemeral = true)
    {
        m_Ephemeral = ephemeral;
        return this;
    }

    public ICommandExecutorBuilder AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            throw new ArgumentException("Parameter name cannot be null or whitespace.", nameof(paramName));
        if (predicate is null)
            throw new ArgumentNullException(nameof(predicate));

        m_Configurators.Add(svc => svc.AddValidator(paramName, predicate, errorMessage));
        return this;
    }

    public ICommandExecutorBuilder ValidateServer(bool validate)
    {
        m_Configurators.Add(svc => svc.ValidateServer = validate);
        return this;
    }

    public ICommandExecutorService Build()
    {
        if (m_InteractionContext is null)
            throw new InvalidOperationException("Interaction context must be provided.");

        var executor = ActivatorUtilities.CreateInstance<CommandExecutorService>(m_ServiceProvider);

        executor.Context = m_InteractionContext;
        executor.Parameters = m_Params;
        executor.CommandName = m_CommandName ?? string.Empty;
        executor.IsResponseEphemeral = m_Ephemeral;

        foreach (var configure in m_Configurators)
            configure(executor);

        return executor;
    }

    public ICommandExecutorBuilder WithParameters(params IEnumerable<(string Key, object Value)> parameters)
    {
        foreach (var param in parameters)
        {
            m_Params.Add(param.Key, param.Value);
        }
        return this;
    }
}

public static class CommandExecutorBuilderServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecutorBuilder(this IServiceCollection services)
    {
        services.AddTransient<ICommandExecutorBuilder, CommandExecutorBuilder>();
        return services;
    }
}
