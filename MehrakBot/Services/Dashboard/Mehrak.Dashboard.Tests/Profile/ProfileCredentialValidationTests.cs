using System.Net;
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
/// Add/Update take LtUid and LToken as separate inputs. Malformed credentials are rejected before any HTTP request,
/// encryption, or persistence: a zero or long-overflowing UID is refused by the controller before the (long) cast,
/// and a malformed token is refused by GameRoleApiService before Cookie-header construction. Only the entered token
/// reaches the encryptor, and error payloads never echo it.
/// </summary>
[TestFixture]
public class ProfileCredentialValidationTests
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
    private const string ValidToken = "v2_abcDEF123-_.=~";
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

    private void SeedProfile(int profileId, ulong ltUid)
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
                    LToken = "enc:old"
                }
            ]
        });
        m_Db.SaveChanges();
        m_Db.ChangeTracker.Clear();
    }

    [Test]
    public async Task AddProfile_SeparateValues_SendsAndEncryptsOnlyEnteredToken()
    {
        const ulong ltUid = 777UL;
        m_Encryption.Setup(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("enc:new");

        var result = await m_Controller.AddProfile(new AddProfileRequest
        {
            LtUid = ltUid,
            LToken = ValidToken,
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
        Assert.That(m_Handler.CookieHeaders, Has.Count.EqualTo(1));
        Assert.That(m_Handler.CookieHeaders[0], Is.EqualTo($"ltoken_v2={ValidToken}; ltuid_v2={ltUid}"));
        m_Encryption.Verify(e => e.Encrypt(ValidToken, Passphrase), Times.Once);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.Multiple(() =>
        {
            Assert.That((ulong)row.LtUid, Is.EqualTo(ltUid));
            Assert.That(row.LToken, Is.EqualTo("enc:new"));
        });
    }

    [Test]
    [TestCase(0UL, Description = "Zero would persist an unusable credential UID")]
    [TestCase(9223372036854775808UL, Description = "One above long.MaxValue overflows the persisted cast")]
    [TestCase(ulong.MaxValue, Description = "Max ulong overflows the persisted cast")]
    public async Task AddProfile_InvalidLtUid_RejectedBeforeUpstreamEncryptionOrPersistence(ulong ltUid)
    {
        var result = await m_Controller.AddProfile(new AddProfileRequest
        {
            LtUid = ltUid,
            LToken = ValidToken,
            Passphrase = Passphrase
        });

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(await m_Db.UserProfiles.CountAsync(), Is.EqualTo(0));
        Assert.That(JsonSerializer.Serialize(((BadRequestObjectResult)result).Value), Does.Not.Contain(ValidToken));
    }

    [Test]
    public async Task AddProfile_MalformedLToken_RejectedBeforeHttpEncryptionOrPersistence()
    {
        const string malformedToken = "C4N4RY TOKEN";

        var result = await m_Controller.AddProfile(new AddProfileRequest
        {
            LtUid = 777UL,
            LToken = malformedToken,
            Passphrase = Passphrase
        });

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        // GameRoleApiService rejects the malformed token before building the Cookie header.
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(await m_Db.UserProfiles.CountAsync(), Is.EqualTo(0));
        Assert.That(JsonSerializer.Serialize(objectResult.Value), Does.Not.Contain("C4N4RY"));
    }

    [Test]
    public async Task UpdateProfile_MalformedLToken_RejectedBeforeHttpEncryptionOrPersistence()
    {
        const ulong ltUid = 111UL;
        SeedProfile(1, ltUid);

        var result = await m_Controller.UpdateProfile(1, new UpdateProfileRequest
        {
            LToken = "C4N4RY TOKEN",
            Passphrase = Passphrase
        });

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult!.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(m_Handler.CookieHeaders, Is.Empty);
        m_Encryption.Verify(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        var row = await m_Db.UserProfiles.SingleAsync(p => p.UserId == (long)UserId);
        Assert.That(row.LToken, Is.EqualTo("enc:old"));
        Assert.That(JsonSerializer.Serialize(objectResult.Value), Does.Not.Contain("C4N4RY"));
    }
}
