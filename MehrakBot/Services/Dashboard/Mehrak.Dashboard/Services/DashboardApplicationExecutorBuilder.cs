using Mehrak.Dashboard.Auth;

namespace Mehrak.Dashboard.Services;

public interface IDashboardApplicationExecutorBuilder
{
    IDashboardApplicationExecutorBuilder WithDiscordUserId(ulong userId);

    IDashboardApplicationExecutorBuilder WithParameters(IEnumerable<(string Key, object Value)> parameters);

    IDashboardApplicationExecutorBuilder WithCommandName(string commandName);

    IDashboardApplicationExecutorBuilder AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null);

    IDashboardApplicationExecutorService Build();
}

internal class DashboardApplicationExecutorBuilder : IDashboardApplicationExecutorBuilder
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly List<Action<DashboardApplicationExecutorService>> m_Configurators = [];
    private readonly Dictionary<string, object> m_Parameters = [];

    private ulong m_DiscordUserId;
    private string? m_CommandName;

    public DashboardApplicationExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public IDashboardApplicationExecutorBuilder WithDiscordUserId(ulong userId)
    {
        m_DiscordUserId = userId;
        return this;
    }

    public IDashboardApplicationExecutorBuilder WithParameters(IEnumerable<(string Key, object Value)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            m_Parameters[key] = value;
        }
        return this;
    }

    public IDashboardApplicationExecutorBuilder WithCommandName(string commandName)
    {
        m_CommandName = commandName;
        return this;
    }

    public IDashboardApplicationExecutorBuilder AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null)
    {
        m_Configurators.Add(service => service.AddValidator(paramName, predicate, errorMessage));
        return this;
    }

    public IDashboardApplicationExecutorService Build()
    {
        var profileAuthService = m_ServiceProvider.GetRequiredService<IDashboardProfileAuthenticationService>();
        var logger = m_ServiceProvider.GetRequiredService<ILogger<DashboardApplicationExecutorService>>();

        var executor = new DashboardApplicationExecutorService(
            m_ServiceProvider,
            profileAuthService,
            logger
        )
        {
            DiscordUserId = m_DiscordUserId,
            CommandName = m_CommandName ?? throw new InvalidOperationException("Command name must be set."),
            Parameters = m_Parameters
        };

        foreach (var configure in m_Configurators)
            configure(executor);

        return executor;
    }
}

public static class DashboardApplicationExecutorServiceCollectionExtensions
{
    public static IServiceCollection AddDashboardApplicationExecutor(this IServiceCollection services)
    {
        services.AddTransient<IDashboardApplicationExecutorBuilder, DashboardApplicationExecutorBuilder>();
        services.AddTransient<IDashboardApplicationExecutorService, DashboardApplicationExecutorService>();
        return services;
    }
}
