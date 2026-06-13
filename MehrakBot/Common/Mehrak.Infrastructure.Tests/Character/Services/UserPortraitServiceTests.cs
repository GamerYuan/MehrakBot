using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Services;
using Mehrak.Infrastructure.Character;
using Mehrak.Infrastructure.Character.Models;
using Mehrak.Infrastructure.Character.Services;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Mehrak.Infrastructure.Tests.Character.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class UserPortraitServiceTests : IDisposable
{
    private readonly TestDbContextFactory m_DbFactory = new();
    private readonly Mock<IAmazonS3> m_MockS3 = new();
    private UserPortraitService m_Service = null!;

    public void Dispose()
    {
        m_DbFactory.Dispose();
    }

    private void SetupService()
    {
        m_Service = new UserPortraitService(
            CreateScopeFactory(),
            m_MockS3.Object,
            Options.Create(new UserPortraitStorageConfig { Bucket = "test-bucket" }),
            NullLogger<UserPortraitService>.Instance);
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

    private static async Task SeedCharacterAsync(CharacterDbContext context, Game game, string name)
    {
        context.Characters.Add(new CharacterModel { Game = game, Name = name });
        await context.SaveChangesAsync();
    }

    private static async Task<UserPortraitUpload> SeedPortraitAsync(
        CharacterDbContext context,
        long discordId,
        Game game,
        string characterName,
        string sha256 = "abc123",
        string s3Key = "123/abc123.png",
        bool isActive = false)
    {
        var upload = new UserPortraitUpload
        {
            Id = Guid.CreateVersion7(),
            DiscordUserId = discordId,
            Game = game,
            CharacterName = characterName,
            SHA256Hash = sha256,
            S3Key = s3Key,
            IsActive = isActive,
            Config = new UserPortraitConfigModel { Id = Guid.NewGuid() }
        };
        context.UserPortraitUploads.Add(upload);
        await context.SaveChangesAsync();
        return upload;
    }

    #region GetPortraitImageAsync

    [Test]
    public async Task GetPortraitImageAsync_ExistingPortrait_ReturnsStream()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", s3Key: "100/abc.png");
        }

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var responseStream = new MemoryStream(imageBytes);
        m_MockS3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                HttpStatusCode = System.Net.HttpStatusCode.OK,
                ResponseStream = responseStream
            });

        var result = await m_Service.GetPortraitImageAsync(100L, portrait.Id);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.ContentType, Is.EqualTo("image/png"));
            Assert.That(result.Content.Length, Is.EqualTo(imageBytes.Length));
        });
    }

    [Test]
    public async Task GetPortraitImageAsync_NonExistentPortrait_ReturnsNull()
    {
        SetupService();

        var result = await m_Service.GetPortraitImageAsync(100L, Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPortraitImageAsync_WrongUser_ReturnsNull()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden");
        }

        var result = await m_Service.GetPortraitImageAsync(200L, portrait.Id);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPortraitImageAsync_S3Throws_ReturnsNull()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", s3Key: "100/abc.png");
        }

        m_MockS3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("S3 error"));

        var result = await m_Service.GetPortraitImageAsync(100L, portrait.Id);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetPortraitImageAsync_PngKey_ReturnsPngContentType()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", s3Key: "100/hash.png");
        }

        m_MockS3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                HttpStatusCode = System.Net.HttpStatusCode.OK,
                ResponseStream = new MemoryStream(new byte[] { 1 })
            });

        var result = await m_Service.GetPortraitImageAsync(100L, portrait.Id);

        Assert.That(result!.ContentType, Is.EqualTo("image/png"));
    }

    [Test]
    public async Task GetPortraitImageAsync_JpgKey_ReturnsJpgContentType()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", s3Key: "100/hash.jpg");
        }

        m_MockS3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse
            {
                HttpStatusCode = System.Net.HttpStatusCode.OK,
                ResponseStream = new MemoryStream(new byte[] { 1 })
            });

        var result = await m_Service.GetPortraitImageAsync(100L, portrait.Id);

        Assert.That(result!.ContentType, Is.EqualTo("image/jpeg"));
    }

    #endregion

    #region SetActivePortraitAsync

    [Test]
    public async Task SetActivePortraitAsync_ValidPortrait_SetsIsActiveTrue()
    {
        SetupService();
        UserPortraitUpload target;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "hash1", isActive: false);
            target = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "hash2", isActive: false);
            await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "hash3", isActive: true);
        }

        var result = await m_Service.SetActivePortraitAsync(100L, target.Id);

        Assert.That(result, Is.True);

        await using var verifyCtx = CreateContext();
        var portraits = await verifyCtx.UserPortraitUploads
            .Where(u => u.DiscordUserId == 100L && u.Game == Game.Genshin && u.CharacterName == "Raiden")
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(portraits.Single(p => p.Id == target.Id).IsActive, Is.True);
            Assert.That(portraits.Where(p => p.Id != target.Id).All(p => !p.IsActive), Is.True);
        });
    }

    [Test]
    public async Task SetActivePortraitAsync_SinglePortrait_SetsActive()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "hash1", isActive: false);
        }

        var result = await m_Service.SetActivePortraitAsync(100L, portrait.Id);

        Assert.That(result, Is.True);

        await using var verifyCtx = CreateContext();
        var entity = await verifyCtx.UserPortraitUploads.FirstAsync(u => u.Id == portrait.Id);
        Assert.That(entity.IsActive, Is.True);
    }

    [Test]
    public async Task SetActivePortraitAsync_NonExistentPortrait_ReturnsFalse()
    {
        SetupService();

        var result = await m_Service.SetActivePortraitAsync(100L, Guid.NewGuid());

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetActivePortraitAsync_WrongUser_ReturnsFalse()
    {
        SetupService();
        UserPortraitUpload portrait;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            portrait = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden");
        }

        var result = await m_Service.SetActivePortraitAsync(200L, portrait.Id);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SetActivePortraitAsync_OnlyDeactivatesSameCharacter()
    {
        SetupService();
        UserPortraitUpload target;
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            await SeedCharacterAsync(ctx, Game.Genshin, "Yae");
            await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "r1", isActive: true);
            target = await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "r2", isActive: false);
            await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Yae", sha256: "y1", isActive: true);
        }

        var result = await m_Service.SetActivePortraitAsync(100L, target.Id);

        Assert.That(result, Is.True);

        await using var verifyCtx = CreateContext();
        var raidenPortraits = await verifyCtx.UserPortraitUploads
            .Where(u => u.DiscordUserId == 100L && u.CharacterName == "Raiden")
            .ToListAsync();
        var yaePortrait = await verifyCtx.UserPortraitUploads
            .FirstAsync(u => u.DiscordUserId == 100L && u.CharacterName == "Yae");

        Assert.Multiple(() =>
        {
            Assert.That(raidenPortraits.Single(p => p.Id == target.Id).IsActive, Is.True);
            Assert.That(raidenPortraits.Where(p => p.Id != target.Id).All(p => !p.IsActive), Is.True);
            Assert.That(yaePortrait.IsActive, Is.True);
        });
    }

    #endregion

    #region UploadPortraitAsync — IsActive auto-set

    [Test]
    public async Task UploadPortraitAsync_FirstPortrait_SetsIsActiveTrue()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
        }

        m_MockS3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });

        var result = await m_Service.UploadPortraitAsync(
            100L, Game.Genshin, "Raiden", new MemoryStream(), "hash1", "png");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Portrait!.IsActive, Is.True);
        });
    }

    [Test]
    public async Task UploadPortraitAsync_SubsequentPortrait_SetsIsActiveFalse()
    {
        SetupService();
        await using (var ctx = CreateContext())
        {
            await SeedCharacterAsync(ctx, Game.Genshin, "Raiden");
            await SeedPortraitAsync(ctx, 100L, Game.Genshin, "Raiden", sha256: "existing");
        }

        m_MockS3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });

        var result = await m_Service.UploadPortraitAsync(
            100L, Game.Genshin, "Raiden", new MemoryStream(), "newhash", "png");

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Portrait!.IsActive, Is.False);
        });
    }

    #endregion
}
