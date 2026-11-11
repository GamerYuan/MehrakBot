#region

using Mehrak.Dashboard.Asset;
using Mehrak.Dashboard.Asset.Models;
using Mehrak.Domain.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace Mehrak.Dashboard.Tests.Asset;

[TestFixture]
public class AssetCleanupControllerTests
{
    private Mock<IAssetSuffixCleanupService> m_CleanupService = null!;
    private AssetCleanupController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        m_CleanupService = new Mock<IAssetSuffixCleanupService>();
        m_Controller = new AssetCleanupController(
            m_CleanupService.Object,
            NullLogger<AssetCleanupController>.Instance);
    }

    [Test]
    public void Controller_IsSuperAdminOnlyOnAssetsRoute()
    {
        var authorize = (AuthorizeAttribute)typeof(AssetCleanupController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Single();
        var route = (RouteAttribute)typeof(AssetCleanupController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Single();
        var httpPost = (HttpPostAttribute)typeof(AssetCleanupController)
            .GetMethod(nameof(AssetCleanupController.CleanupBySuffix))!
            .GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(authorize.Policy, Is.EqualTo("RequireSuperAdmin"));
            Assert.That(route.Template, Is.EqualTo("assets"));
            Assert.That(httpPost.Template, Is.EqualTo("cleanup-by-suffix"));
        });
    }

    [Test]
    public async Task CleanupBySuffix_DefaultsToDryRun()
    {
        AssetSuffixCleanupRequest? captured = null;
        m_CleanupService.Setup(service => service.CleanupBySuffixAsync(
                It.IsAny<AssetSuffixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssetSuffixCleanupRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AssetSuffixCleanupResult(true, "_bg.png", 1, 0, 0, null, []));

        var result = await m_Controller.CleanupBySuffix(
            new CleanupBySuffixRequest { Suffix = "_bg.png" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<OkObjectResult>());
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Suffix, Is.EqualTo("_bg.png"));
            Assert.That(captured.DryRun, Is.True);
        });
    }

    [Test]
    public async Task CleanupBySuffix_ForwardsExecuteAndCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        AssetSuffixCleanupRequest? captured = null;
        m_CleanupService.Setup(service => service.CleanupBySuffixAsync(
                It.IsAny<AssetSuffixCleanupRequest>(), cancellation.Token))
            .Callback<AssetSuffixCleanupRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AssetSuffixCleanupResult(false, "_bg.png", 0, 0, 0, null, []));

        await m_Controller.CleanupBySuffix(
            new CleanupBySuffixRequest { Suffix = "_bg.png", DryRun = false }, cancellation.Token);

        m_CleanupService.Verify(service => service.CleanupBySuffixAsync(
            It.IsAny<AssetSuffixCleanupRequest>(), cancellation.Token), Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.DryRun, Is.False);
        });
    }

    [Test]
    public async Task CleanupBySuffix_NullBody_ReturnsBadRequestWithoutCallingService()
    {
        var result = await m_Controller.CleanupBySuffix(null!, CancellationToken.None);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        m_CleanupService.Verify(service => service.CleanupBySuffixAsync(
            It.IsAny<AssetSuffixCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task CleanupBySuffix_InvalidSuffix_ReturnsBadRequest()
    {
        m_CleanupService.Setup(service => service.CleanupBySuffixAsync(
                It.IsAny<AssetSuffixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidSuffix, "A non-whitespace suffix is required."));

        var result = await m_Controller.CleanupBySuffix(
            new CleanupBySuffixRequest { Suffix = " " }, CancellationToken.None);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(400));
            Assert.That(objectResult.Value, Is.Not.Null);
        });
    }

    [TestCase(AssetCleanupErrorCodes.BackupBucketConflict)]
    [TestCase(AssetCleanupErrorCodes.SourceBucketConflict)]
    [TestCase(AssetCleanupErrorCodes.LifecycleConflict)]
    public async Task CleanupBySuffix_ConfigurationConflict_ReturnsConflict(string errorCode)
    {
        m_CleanupService.Setup(service => service.CleanupBySuffixAsync(
                It.IsAny<AssetSuffixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(errorCode, "Buckets or lifecycle rules conflict."));

        var result = await m_Controller.CleanupBySuffix(
            new CleanupBySuffixRequest { Suffix = "_bg.png", DryRun = false }, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task CleanupBySuffix_StorageFailure_ReturnsBadGateway()
    {
        m_CleanupService.Setup(service => service.CleanupBySuffixAsync(
                It.IsAny<AssetSuffixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.ListingFailed, "Listing failed."));

        var result = await m_Controller.CleanupBySuffix(
            new CleanupBySuffixRequest { Suffix = "_bg.png", DryRun = false }, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(502));
    }
}
