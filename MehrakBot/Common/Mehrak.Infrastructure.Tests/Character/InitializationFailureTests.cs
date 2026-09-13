using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
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
    public void FailedContentInitialization_PreventsHostStartup(bool aliases)
    {
        var cache = new Mock<ICharacterCacheService>();
        cache.Setup(service => service.UpdateAllCharactersAsync()).ThrowsAsync(new InvalidOperationException("cache unavailable"));
        var alias = new Mock<IAliasService>();
        alias.Setup(service => service.UpsertAliases(It.IsAny<Game>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var builder = Host.CreateApplicationBuilder();
        var assets = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(assets);
        var aliasFile = Path.Combine(assets, "aliases.json");
        File.WriteAllText(aliasFile, """{"game":1,"aliases":[{"name":"Raiden Shogun","alias":["raiden"]}]}""");
        builder.Services.AddSingleton<IHostedService>(services => aliases
            ? ActivatorUtilities.CreateInstance<AliasInitializationService>(services,
                NullLogger<AliasInitializationService>.Instance, alias.Object, assets)
            : new CharacterInitializationService(services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<CharacterInitializationService>.Instance, cache.Object, assets));
        using var host = builder.Build();

        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        }
        finally
        {
            File.Delete(aliasFile);
            Directory.Delete(assets);
        }
    }
}
