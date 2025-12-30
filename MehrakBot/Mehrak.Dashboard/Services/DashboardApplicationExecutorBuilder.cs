using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Dashboard.Services;

public interface IDashboardApplicationExecutorBuilder
{
    IDashboardApplicationExecutorBuilder<TContext> For<TContext>() where TContext : IApplicationContext;
}

public interface IDashboardApplicationExecutorBuilder<TContext>
    where TContext : IApplicationContext
{
    IDashboardApplicationExecutorBuilder<TContext> WithDiscordUserId(ulong userId);

    IDashboardApplicationExecutorBuilder<TContext> WithApplicationContext(TContext context);

    IDashboardApplicationExecutorBuilder<TContext> AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null);

    IDashboardApplicationExecutorService<TContext> Build();
}

internal class DashboardApplicationExecutorBuilder : IDashboardApplicationExecutorBuilder
{
    private readonly IServiceProvider m_ServiceProvider;

    public DashboardApplicationExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public IDashboardApplicationExecutorBuilder<TContext> For<TContext>() where TContext : IApplicationContext
    {
        return ActivatorUtilities.CreateInstance<DashboardApplicationExecutorBuilder<TContext>>(m_ServiceProvider);
    }
}

internal class DashboardApplicationExecutorBuilder<TContext> : IDashboardApplicationExecutorBuilder<TContext>
    where TContext : IApplicationContext
{
    private readonly IServiceProvider m_ServiceProvider;
    private readonly List<Action<DashboardApplicationExecutorService<TContext>>> m_Configurators = [];

    private ulong m_DiscordUserId;
    private TContext? m_ApplicationContext;

    public DashboardApplicationExecutorBuilder(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public IDashboardApplicationExecutorBuilder<TContext> WithDiscordUserId(ulong userId)
    {
        if (userId == 0)
            throw new ArgumentOutOfRangeException(nameof(userId));

        m_DiscordUserId = userId;
        return this;
    }

    public IDashboardApplicationExecutorBuilder<TContext> WithApplicationContext(TContext context)
    {
        m_ApplicationContext = context ?? throw new ArgumentNullException(nameof(context));
        return this;
    }

    public IDashboardApplicationExecutorBuilder<TContext> AddValidator<TParam>(string paramName, Predicate<TParam> predicate,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(paramName))
            throw new ArgumentException("Parameter name cannot be null or whitespace.", nameof(paramName));
        if (predicate is null)
            throw new ArgumentNullException(nameof(predicate));

        m_Configurators.Add(exec => exec.AddValidator(paramName, predicate, errorMessage));
        return this;
    }

    public IDashboardApplicationExecutorService<TContext> Build()
    {
        if (m_DiscordUserId == 0)
            throw new InvalidOperationException("Discord user id must be provided before building an executor.");
        if (m_ApplicationContext is null)
            throw new InvalidOperationException("Application context must be provided before building an executor.");

        var executor = ActivatorUtilities.CreateInstance<DashboardApplicationExecutorService<TContext>>(m_ServiceProvider);

        executor.DiscordUserId = m_DiscordUserId;
        executor.ApplicationContext = m_ApplicationContext;

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
        services.AddTransient(typeof(IDashboardApplicationExecutorBuilder<>), typeof(DashboardApplicationExecutorBuilder<>));
        services.AddTransient(typeof(IDashboardApplicationExecutorService<>), typeof(DashboardApplicationExecutorService<>));
        return services;
    }
}
