using System.Security.Claims;
using System.Text.Json;
using Mehrak.Dashboard.Character;
using Mehrak.Dashboard.Character.Models;
using Mehrak.Dashboard.Shared.Auth;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Mehrak.Dashboard.Tests.Character;

[TestFixture]
public class AliasControllerTests
{
    private Mock<IAliasService> m_AliasService = null!;
    private ServiceProvider m_ServiceProvider = null!;
    private AliasController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        m_AliasService = new Mock<IAliasService>();
        m_AliasService.Setup(service => service.GetAliases(Game.Genshin))
            .Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder()
            .AddPolicy(GameAuthorization.Policy, policy => policy.AddRequirements(new GameWriteRequirement()));
        services.AddSingleton<IAuthorizationHandler, GameWriteAuthorizationHandler>();
        m_ServiceProvider = services.BuildServiceProvider();
        m_Controller = new AliasController(m_AliasService.Object, NullLogger<AliasController>.Instance)
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
    public void TearDown() => m_ServiceProvider.Dispose();

    [Test]
    public async Task ListAliases_PreservesAliasAndCharacterCapitalization()
    {
        m_AliasService.Setup(service => service.GetAliases(Game.Genshin))
            .Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ei"] = "Raiden Shogun",
                ["RAIDEN"] = "Raiden Shogun"
            });

        var result = await m_Controller.ListAliases("Genshin");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var value = ((OkObjectResult)result).Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.That(json.RootElement.GetProperty("Raiden Shogun").EnumerateArray()
            .Select(alias => alias.GetString()), Is.EquivalentTo(new[] { "Ei", "RAIDEN" }));
    }

    [Test]
    public async Task AddAliases_PreservesCapitalizationAndCleansWhitespace()
    {
        m_AliasService.Setup(service => service.UpsertAliases(Game.Genshin, It.IsAny<Dictionary<string, string>>()))
            .Returns(Task.CompletedTask);

        var result = await m_Controller.AddAliases("Genshin", new AddAliasRequest
        {
            Character = "  Raiden Shogun\r\n",
            Aliases = ["  Ei\r\n", "EI", "RAIDEN"]
        });

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        m_AliasService.Verify(service => service.UpsertAliases(Game.Genshin,
            It.Is<Dictionary<string, string>>(aliases =>
                aliases.Count == 2 &&
                aliases.Keys.Contains("Ei", StringComparer.Ordinal) &&
                aliases.Keys.Contains("RAIDEN", StringComparer.Ordinal) &&
                aliases.Values.All(character => character == "Raiden Shogun"))), Times.Once);
    }

    [Test]
    public async Task AddAliases_CaseVariantOfExistingAlias_ReturnsConflict()
    {
        m_AliasService.Setup(service => service.GetAliases(Game.Genshin))
            .Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Ei"] = "Raiden Shogun" });

        var result = await m_Controller.AddAliases("Genshin", new AddAliasRequest
        {
            Character = "Raiden Shogun",
            Aliases = ["EI"]
        });

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        m_AliasService.Verify(service => service.UpsertAliases(It.IsAny<Game>(),
            It.IsAny<Dictionary<string, string>>()), Times.Never);
    }
}
