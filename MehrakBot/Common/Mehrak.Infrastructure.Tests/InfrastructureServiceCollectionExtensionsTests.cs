using Mehrak.Infrastructure.Auth.Services;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Storage;
using Mehrak.Infrastructure.User.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mehrak.Infrastructure.Tests;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
internal sealed class InfrastructureServiceCollectionExtensionsTests
{
    [Test]
    public void AddInfrastructureServices_DoesNotRegisterProcessOwnedHostedServices()
    {
        var services = new ServiceCollection();

        services.AddInfrastructureServices();

        Assert.That(GetHostedServiceTypes(services), Is.Empty);
    }

    [Test]
    public void ApplicationHostedServices_RegisterOnlyApplicationOwnedInitialization()
    {
        var services = new ServiceCollection();

        services.AddApplicationInfrastructureHostedServices();

        Assert.That(GetHostedServiceTypes(services), Is.EqualTo(
        [
            typeof(AttachmentStorageInitializer),
            typeof(CharacterInitializationService),
            typeof(AliasInitializationService)
        ]));
    }

    [Test]
    public void DashboardHostedServices_RegisterOnlySessionCleanup()
    {
        var services = new ServiceCollection();

        services.AddDashboardInfrastructureHostedServices();

        Assert.That(GetHostedServiceTypes(services), Is.EqualTo([typeof(SessionCleanupService)]));
    }

    private static Type[] GetHostedServiceTypes(IServiceCollection services) =>
        [..
            services
                .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                .Select(descriptor => descriptor.ImplementationType)
                .Where(type => type is not null)
                .Cast<Type>()];
}
