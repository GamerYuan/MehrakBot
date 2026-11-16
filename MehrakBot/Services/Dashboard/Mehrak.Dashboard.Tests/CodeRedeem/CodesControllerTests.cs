using System.Security.Claims;
using System.Text.Json;
using Mehrak.Dashboard.CodeRedeem;
using Mehrak.Dashboard.CodeRedeem.Models;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Infrastructure.CodeRedeem;
using Mehrak.Infrastructure.CodeRedeem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mehrak.Dashboard.Tests.CodeRedeem;

[TestFixture]
public class CodesControllerTests
{
    private ServiceProvider m_ServiceProvider = null!;
    private SavingCodeContext m_Context = null!;
    private CodesController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder()
            .AddPolicy(GameAuthorization.Policy, policy => policy.AddRequirements(new GameWriteRequirement()));
        services.AddSingleton<IAuthorizationHandler, GameWriteAuthorizationHandler>();
        m_ServiceProvider = services.BuildServiceProvider();
        m_Context = new SavingCodeContext(new DbContextOptionsBuilder<CodeRedeemDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        m_Controller = new CodesController(m_Context, NullLogger<CodesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = m_ServiceProvider,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, "superadmin")], "TestAuth"))
                }
            }
        };
    }

    [TearDown]
    public void TearDown()
    {
        m_Context.Dispose();
        m_ServiceProvider.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mutation_SaveFails_ReturnsSanitized500WithoutRetry(bool add)
    {
        await SeedCodesAsync();
        m_Context.FailSave = true;

        var result = await MutateAsync(add, "Genshin", ["EXISTING", "NEW"]);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.That(json.RootElement.GetProperty("error").GetString(), Is.EqualTo("Failed to update codes."));
        Assert.That(m_Context.SaveAttempts, Is.EqualTo(1));
        m_Context.ChangeTracker.Clear();
        Assert.That(await m_Context.Codes.Select(code => code.Code).ToListAsync(),
            Is.EquivalentTo(new[] { "EXISTING", "OTHERGAME" }));
    }

    [Test]
    public async Task AddCodes_NormalizesAndDeduplicates_PersistsWith204()
    {
        await SeedCodesAsync();

        var result = await MutateAsync(true, "Genshin", [" EXISTING\r\n", " new\r\n", "NEW", " ", ""]);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(m_Context.SaveAttempts, Is.EqualTo(1));
        m_Context.ChangeTracker.Clear();
        Assert.That(await m_Context.Codes.Where(code => code.Game == Game.Genshin)
            .Select(code => code.Code).ToListAsync(), Is.EquivalentTo(new[] { "EXISTING", "NEW" }));
        Assert.That(await m_Context.Codes.CountAsync(code => code.Game == Game.HonkaiStarRail), Is.EqualTo(1));
    }

    [Test]
    public async Task RemoveCodes_NormalizesAndDeduplicates_PersistsWith204()
    {
        await SeedCodesAsync();

        var result = await MutateAsync(false, "Genshin", [" EXISTING\r\n", "EXISTING", "MISSING", " ", ""]);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(m_Context.SaveAttempts, Is.EqualTo(1));
        m_Context.ChangeTracker.Clear();
        Assert.That(await m_Context.Codes.Select(code => code.Code).ToListAsync(),
            Is.EquivalentTo(new[] { "OTHERGAME" }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mutation_NoChanges_Returns204(bool add)
    {
        await SeedCodesAsync();

        var result = await MutateAsync(add, "Genshin", [add ? "EXISTING" : "MISSING"]);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        Assert.That(m_Context.SaveAttempts, Is.EqualTo(1));
        Assert.That(await m_Context.Codes.CountAsync(), Is.EqualTo(2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Mutation_Unauthorized_DoesNotSave(bool add)
    {
        m_Controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("perm", "game_write:honkaistarrail")], "TestAuth"));

        var result = await MutateAsync(add, "Genshin", ["CODE"]);

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        Assert.That(m_Context.SaveAttempts, Is.Zero);
        Assert.That(m_Context.ChangeTracker.Entries(), Is.Empty);
    }

    [TestCase(true, "Unknown", "CODE")]
    [TestCase(false, "Unknown", "CODE")]
    [TestCase(true, "Genshin", " \r\n")]
    [TestCase(false, "Genshin", " \r\n")]
    public async Task Mutation_InvalidGameOrEmptyCodes_DoesNotSave(bool add, string game, string code)
    {
        var result = await MutateAsync(add, game, [code]);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(m_Context.SaveAttempts, Is.Zero);
    }

    private Task<IActionResult> MutateAsync(bool add, string game, List<string> codes) => add
        ? m_Controller.AddCodes(game, new AddCodesRequest { Codes = codes })
        : m_Controller.RemoveCodes(game, new RemoveCodesRequest { Codes = codes });

    private async Task SeedCodesAsync()
    {
        m_Context.Codes.AddRange(
            new CodeRedeemModel { Game = Game.Genshin, Code = "EXISTING" },
            new CodeRedeemModel { Game = Game.HonkaiStarRail, Code = "OTHERGAME" });
        await m_Context.SaveChangesAsync();
        m_Context.SaveAttempts = 0;
        m_Context.ChangeTracker.Clear();
    }

    private sealed class SavingCodeContext(DbContextOptions<CodeRedeemDbContext> options) : CodeRedeemDbContext(options)
    {
        public bool FailSave { get; set; }
        public int SaveAttempts { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (FailSave)
                throw new DbUpdateException("Provider details that must not appear in the response.");

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
