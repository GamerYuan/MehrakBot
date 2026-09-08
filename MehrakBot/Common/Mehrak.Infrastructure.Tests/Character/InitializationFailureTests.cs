using Mehrak.Domain.Character;
using Mehrak.Infrastructure.Character.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.Character;

[TestFixture]
internal sealed class InitializationFailureTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void FailedCacheInitialization_PreventsHostStartup(bool aliases)
    {
        var cache = new Mock<ICharacterCacheService>();
        cache.Setup(service => service.UpdateAllCharactersAsync()).ThrowsAsync(new InvalidOperationException("cache unavailable"));
        var alias = new Mock<IAliasService>();
        alias.Setup(service => service.UpdateAllAliasesAsync()).ThrowsAsync(new InvalidOperationException("cache unavailable"));
        var builder = Host.CreateApplicationBuilder();
        var missingAssets = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        builder.Services.AddSingleton<IHostedService>(services => aliases
            ? new AliasInitializationService(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<AliasInitializationService>.Instance, alias.Object, missingAssets)
            : new CharacterInitializationService(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CharacterInitializationService>.Instance, cache.Object, missingAssets));
        using var host = builder.Build();

        Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
    }
}
