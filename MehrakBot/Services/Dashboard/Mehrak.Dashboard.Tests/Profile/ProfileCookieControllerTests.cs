﻿﻿using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Mehrak.Dashboard.Profile;
using Mehrak.Dashboard.Profile.Models;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.GameRole;
using Mehrak.Infrastructure.User;
using Mehrak.Infrastructure.User.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Dashboard.Tests.Profile;

/// <summary>
/// CookieString requests must extract the credential pair before any upstream
/// call, encryption, or persistence: only the extracted ltoken/UID reach the
/// GameRole API and the encryptor, an update whose cookie UID differs from the
/// stored UID is rejected before upstream/persistence, and failures stay
/// generic without echoing the cookie, token, or passphrase. </summary>
[TestFixture]
public class ProfileCookieControllerTests
{
    private sealed class FakeCacheService : ICacheService
    {
        public Task SetAsync<T>(Domain.Cache.Abstractions.ICacheEntry<T> entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingGameRoleHandler : HttpMessageHandler
    {
        public readonly List<string> CookieHeaders = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CookieHeaders.Add(request.Headers.TryGetValues("Cookie", out var values)
                ? string.Join("|", values)
                : "<missing>");
            const string json = """
                {"retcode":0,"message":"OK","data":{"list":[
                  {"game_biz":"hk4e_global","region":"os_usa","game_uid":"600000001",
                   "nickname":"Tester","level":60,"is_chosen":true,"region_name":"America"}
                ]}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private UserDbContext m_Db = null!;
    private Mock<IEncryptionService> m_Encryption = null!;
    private RecordingGameRoleHandler m_Handler = null!;
    private ProfileController m_Controller = null!;

    private const ulong UserId = 100UL;
    private const string Passphrase = "valid-passphrase-12";

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<UserDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        m_Db = new UserDbContext(options);
        m_Encryption = new Mock<IEncryptionService>();
        m_Handler = new RecordingGameRoleHandler();

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(m_Handler));
        var gameRoleApi = new GameRoleApiService(
            httpFactory.Object, new FakeCacheService(), Mock.Of<ILogger<GameRoleApiService>>());

        m_Controller = new ProfileController(
            m_Db, m_Encryption.Object, new FakeCacheService(), gameRoleApi,
            Mock.Of<ILogger<ProfileController>>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("discord_id", UserId.ToString())], "TestAuth"))
        };
        m_Controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [TearDown]
    public void TearDown()
    {
        m_Db.Dispose();
        m_Handler.Dispose();
    }

    private void SeedProfile(int profileId, ulong ltUid, string ltoken = "enc:old")
    {
        m_Db.Users.Add(new UserModel
        {
            Id = (long)UserId,
            Timestamp = DateTime.UtcNow,
            Profiles =
            [
                new UserProfileModel
                {
                    UserId = (long)UserId,
                    ProfileId = profileId,
                    LtUid = (long)ltUid,
                    LToken = ltoken
                }
            ]
        });
        m_Db.SaveChanges();
        m_Db.ChangeTracker.Clear();
    }

    [Test]
    public async Task AddProfile_CookieString_SendsAndEncryptsOnlyExtractedValues()
    {
        m_Encryption.Setup(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("enc:rotated");

        var result = await m_Controller.AddProfile(new AddProfileRequest
        {
            CookieString = "other=xyz; ltoken_v2=extractedtok123; ltuid_v2=777",
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        // Only the extracted pair reaches HoYoLAB, never the raw cookie string.
        Assert.That(m_Handler.CookieHeaders, Has.Count.EqualTo(1));
        Assert.That(m_Handler.CookieHeaders[0], Is.EqualTo("ltoken_v2=extractedtok123; ltuid_v2=777"));
        // Only the extracted token is encrypted, with the supplied passphrase.
        m_Encryption.Verify(e => e.Encrypt("extractedtok123", Passphrase), Times.Once);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.Multiple(() =>
        {
            Assert.That((ulong)row.LtUid, Is.EqualTo(777UL));
            Assert.That(row.LToken, Is.EqualTo("enc:rotated"));
        });
    }

    [Test]
    public async Task AddProfile_UnparsableCookieString_FailsBeforeUpstreamOrPersistence()
    {
        var result = await m_Controller.AddProfile(new AddProfileRequest
        {
            CookieString = "ltoken_v2=C4N4RY BAD; ltuid_v2=777",
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(await m_Db.UserProfiles.CountAsync(), Is.EqualTo(0));
        Assert.That(JsonSerializer.Serialize(((BadRequestObjectResult)result).Value),
            Does.Not.Contain("C4N4RY"));
    }

    [Test]
    public async Task UpdateProfile_MatchingCookieUid_RotatesExtractedTokenOnly()
    {
        const ulong ltUid = 111UL;
        SeedProfile(1, ltUid);
        m_Encryption.Setup(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("enc:rotated");

        var result = await m_Controller.UpdateProfile(1, new UpdateProfileRequest
        {
            CookieString = $"session=zzz; ltuid_v2={ltUid}; ltoken_v2=newtoken456",
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        Assert.That(m_Handler.CookieHeaders, Has.Count.EqualTo(1));
        Assert.That(m_Handler.CookieHeaders[0], Is.EqualTo($"ltoken_v2=newtoken456; ltuid_v2={ltUid}"));
        m_Encryption.Verify(e => e.Encrypt("newtoken456", Passphrase), Times.Once);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.That(row.LToken, Is.EqualTo("enc:rotated"));
    }

    [Test]
    public async Task UpdateProfile_MismatchedCookieUid_RejectedBeforeUpstreamOrPersistence()
    {
        const ulong storedUid = 111UL;
        SeedProfile(1, storedUid);

        var result = await m_Controller.UpdateProfile(1, new UpdateProfileRequest
        {
            CookieString = "ltoken_v2=othertoken789; ltuid_v2=222",
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        // Rejected before any upstream validation call...
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        // ...and before encryption or persistence.
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.That(row.LToken, Is.EqualTo("enc:old"));
        Assert.That(JsonSerializer.Serialize(((BadRequestObjectResult)result).Value),
            Does.Not.Contain("othertoken789"));
    }

    [Test]
    public async Task UpdateProfile_UnparsableCookieString_FailsWithoutSideEffects()
    {
        SeedProfile(1, 111UL);

        var result = await m_Controller.UpdateProfile(1, new UpdateProfileRequest
        {
            CookieString = "not-a-cookie",
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.That(row.LToken, Is.EqualTo("enc:old"));
    }
}
