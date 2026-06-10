using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class CharacterPortraitConfigServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IDistributedCache> m_MockCache = new();
    private CharacterPortraitConfigService m_Service = null!;

    public void Dispose()
    {
        m_DbFactory.Dispose();
    }

    private void SetupService()
    {
        m_Service = new CharacterPortraitConfigService(
            CreateScopeFactory(),
            m_MockCache.Object,
            NullLogger<CharacterPortraitConfigService>.Instance);
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();

        scopeFactory.Setup(x => x.CreateScope()).Returns(() =>
        {
            var context = m_DbFactory.CreateDbContext<CharacterDbContext>();

            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(x => x.GetService(typeof(CharacterDbContext))).Returns(context);

            var scope = new Mock<IServiceScope>();
            scope.Setup(x => x.ServiceProvider).Returns(serviceProvider.Object);

            return scope.Object;
        });

        return scopeFactory.Object;
    }

    private CharacterDbContext CreateContext()
    {
        return m_DbFactory.CreateDbContext<CharacterDbContext>();
    }

    private static async Task SeedServerIdAsync(CharacterDbContext context, Game game, int serverId, string name)
    {
        var character = new CharacterModel
        {
            Game = game,
            Name = name
        };
        context.Characters.Add(character);
        await context.SaveChangesAsync();

        context.CharacterServerIds.Add(new CharacterServerIdModel
        {
            CharacterId = character.Id,
            ServerId = serverId
        });
        await context.SaveChangesAsync();
    }

    #region Insert with all null fields

    [Test]
    public async Task UpsertConfigAsync_AllNullFields_InsertsEntityWithNulls()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.Genshin, 100, "TestCharacter");
        }

        var update = new CharacterPortraitConfigUpdate();

        var result = await m_Service.UpsertConfigAsync(Game.Genshin, 100, update);

        Assert.That(result, Is.True);

        await using var verifyContext = CreateContext();
        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstOrDefaultAsync(c => c.Game == Game.Genshin && c.ServerId == 100);

        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Name, Is.EqualTo("TestCharacter"));
        Assert.That(entity.OffsetX, Is.Null);
        Assert.That(entity.OffsetY, Is.Null);
        Assert.That(entity.TargetScale, Is.Null);
        Assert.That(entity.EnableGradientFade, Is.Null);
        Assert.That(entity.GradientFadeStart, Is.Null);
    }

    #endregion

    #region Insert with mixed null and non-null fields

    [Test]
    public async Task UpsertConfigAsync_MixedNullAndNonNull_InsertsCorrectly()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.HonkaiStarRail, 200, "March7th");
        }

        var update = new CharacterPortraitConfigUpdate
        {
            OffsetX = 10,
            TargetScale = 1.5f,
            EnableGradientFade = true
        };

        var result = await m_Service.UpsertConfigAsync(Game.HonkaiStarRail, 200, update);

        Assert.That(result, Is.True);

        await using var verifyContext = CreateContext();
        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstOrDefaultAsync(c => c.Game == Game.HonkaiStarRail && c.ServerId == 200);

        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.OffsetX, Is.EqualTo(10));
        Assert.That(entity.OffsetY, Is.Null);
        Assert.That(entity.TargetScale, Is.EqualTo(1.5f));
        Assert.That(entity.EnableGradientFade, Is.True);
        Assert.That(entity.GradientFadeStart, Is.Null);
    }

    #endregion

    #region Update existing entity — null overwrites values

    [Test]
    public async Task UpsertConfigAsync_ExistingEntity_AllNullOverwritesValues()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.ZenlessZoneZero, 300, "Nicole");
        }

        var initialUpdate = new CharacterPortraitConfigUpdate
        {
            OffsetX = 5,
            OffsetY = -3,
            TargetScale = 2.0f,
            EnableGradientFade = true,
            GradientFadeStart = 0.5f
        };
        await m_Service.UpsertConfigAsync(Game.ZenlessZoneZero, 300, initialUpdate);

        var nullUpdate = new CharacterPortraitConfigUpdate();
        await m_Service.UpsertConfigAsync(Game.ZenlessZoneZero, 300, nullUpdate);

        await using var verifyContext = CreateContext();
        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstOrDefaultAsync(c => c.Game == Game.ZenlessZoneZero && c.ServerId == 300);

        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.OffsetX, Is.Null);
        Assert.That(entity.OffsetY, Is.Null);
        Assert.That(entity.TargetScale, Is.Null);
        Assert.That(entity.EnableGradientFade, Is.Null);
        Assert.That(entity.GradientFadeStart, Is.Null);
    }

    #endregion

    #region Update existing entity — preserves name

    [Test]
    public async Task UpsertConfigAsync_ExistingEntity_UpdatesName()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.Genshin, 400, "OldName");
        }

        var initialUpdate = new CharacterPortraitConfigUpdate { OffsetX = 1 };
        await m_Service.UpsertConfigAsync(Game.Genshin, 400, initialUpdate);

        await using var verifyContext1 = CreateContext();
        var entity = await verifyContext1.CharacterPortraitConfigs
            .FirstAsync(c => c.Game == Game.Genshin && c.ServerId == 400);
        Assert.That(entity.Name, Is.EqualTo("OldName"));

        var newUpdate = new CharacterPortraitConfigUpdate { OffsetY = 2 };
        await m_Service.UpsertConfigAsync(Game.Genshin, 400, newUpdate);

        await using var verifyContext2 = CreateContext();
        entity = await verifyContext2.CharacterPortraitConfigs
            .FirstAsync(c => c.Game == Game.Genshin && c.ServerId == 400);
        Assert.That(entity.Name, Is.EqualTo("OldName"));
        Assert.That(entity.OffsetY, Is.EqualTo(2));
    }

    #endregion

    #region Server ID not found

    [Test]
    public async Task UpsertConfigAsync_ServerIdNotFound_ReturnsFalse()
    {
        SetupService();

        var update = new CharacterPortraitConfigUpdate { OffsetX = 1 };

        var result = await m_Service.UpsertConfigAsync(Game.Genshin, 999, update);

        Assert.That(result, Is.False);

        await using var verifyContext = CreateContext();
        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstOrDefaultAsync(c => c.Game == Game.Genshin && c.ServerId == 999);

        Assert.That(entity, Is.Null);
    }

    #endregion

    #region Non-null values

    [Test]
    public async Task UpsertConfigAsync_NonNullValues_StoresCorrectly()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.HonkaiImpact3, 500, "Kiana");
        }

        var update = new CharacterPortraitConfigUpdate
        {
            OffsetX = -15,
            OffsetY = 20,
            TargetScale = 3.5f,
            EnableGradientFade = false,
            GradientFadeStart = 0.9f
        };

        var result = await m_Service.UpsertConfigAsync(Game.HonkaiImpact3, 500, update);

        Assert.That(result, Is.True);

        await using var verifyContext = CreateContext();
        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstAsync(c => c.Game == Game.HonkaiImpact3 && c.ServerId == 500);

        Assert.Multiple(() =>
        {
            Assert.That(entity.OffsetX, Is.EqualTo(-15));
            Assert.That(entity.OffsetY, Is.EqualTo(20));
            Assert.That(entity.TargetScale, Is.EqualTo(3.5f));
            Assert.That(entity.EnableGradientFade, Is.False);
            Assert.That(entity.GradientFadeStart, Is.EqualTo(0.9f));
        });
    }

    #endregion

    #region Idempotency — same entity, not duplicated

    [Test]
    public async Task UpsertConfigAsync_CalledTwice_CreatesSingleEntity()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedServerIdAsync(ctx, Game.TearsOfThemis, 600, "Luke");
        }

        var update1 = new CharacterPortraitConfigUpdate { OffsetX = 1 };
        var update2 = new CharacterPortraitConfigUpdate { OffsetY = 2 };

        await m_Service.UpsertConfigAsync(Game.TearsOfThemis, 600, update1);
        await m_Service.UpsertConfigAsync(Game.TearsOfThemis, 600, update2);

        await using var verifyContext = CreateContext();
        var count = await verifyContext.CharacterPortraitConfigs
            .CountAsync(c => c.Game == Game.TearsOfThemis && c.ServerId == 600);

        Assert.That(count, Is.EqualTo(1));

        var entity = await verifyContext.CharacterPortraitConfigs
            .FirstAsync(c => c.Game == Game.TearsOfThemis && c.ServerId == 600);

        Assert.Multiple(() =>
        {
            Assert.That(entity.OffsetX, Is.Null);
            Assert.That(entity.OffsetY, Is.EqualTo(2));
        });
    }

    #endregion
}
