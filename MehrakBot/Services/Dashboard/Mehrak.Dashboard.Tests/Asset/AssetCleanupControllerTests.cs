#region

using System.Text.Json;
using Amazon.S3;
using Mehrak.Dashboard.Asset;
using Mehrak.Dashboard.Asset.Models;
using Mehrak.Domain.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace Mehrak.Dashboard.Tests.Asset;

[TestFixture]
public class AssetCleanupControllerTests
{
    private const string SourcePrefix = "genshin/arcana_";

    private Mock<IAssetPrefixCleanupService> m_CleanupService = null!;
    private AssetCleanupController m_Controller = null!;

    [SetUp]
    public void SetUp()
    {
        m_CleanupService = new Mock<IAssetPrefixCleanupService>();
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
            .GetMethod(nameof(AssetCleanupController.CleanupByPrefix))!
            .GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Single();

        Assert.Multiple(() =>
        {
            Assert.That(authorize.Policy, Is.EqualTo("RequireSuperAdmin"));
            Assert.That(route.Template, Is.EqualTo("assets"));
            Assert.That(httpPost.Template, Is.EqualTo("cleanup-by-prefix"));
        });
    }

    [Test]
    public async Task CleanupByPrefix_DefaultsToDryRun()
    {
        AssetPrefixCleanupRequest? captured = null;
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssetPrefixCleanupRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AssetPrefixCleanupResult(true, SourcePrefix, 1, 0, 0, null, []));

        var result = await m_Controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = SourcePrefix }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<OkObjectResult>());
            var ok = (OkObjectResult)result;
            Assert.That(ok.Value, Is.TypeOf<AssetPrefixCleanupResult>());
            Assert.That(((AssetPrefixCleanupResult)ok.Value!).Prefix, Is.EqualTo(SourcePrefix));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Prefix, Is.EqualTo(SourcePrefix));
            Assert.That(captured.DryRun, Is.True);
        });
    }

    [Test]
    public async Task CleanupByPrefix_ForwardsExecuteAndCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        AssetPrefixCleanupRequest? captured = null;
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), cancellation.Token))
            .Callback<AssetPrefixCleanupRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AssetPrefixCleanupResult(false, SourcePrefix, 0, 0, 0, null, []));

        await m_Controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = SourcePrefix, DryRun = false }, cancellation.Token);

        m_CleanupService.Verify(service => service.CleanupByPrefixAsync(
            It.IsAny<AssetPrefixCleanupRequest>(), cancellation.Token), Times.Once);
        Assert.Multiple(() =>
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.DryRun, Is.False);
        });
    }

    [Test]
    public async Task CleanupByPrefix_NullBody_ReturnsBadRequestWithoutCallingService()
    {
        var result = await m_Controller.CleanupByPrefix(null!, CancellationToken.None);

        var badRequest = (BadRequestObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(badRequest.StatusCode, Is.EqualTo(400));
            Assert.That(JsonSerializer.Serialize(badRequest.Value), Does.Contain(AssetCleanupErrorCodes.InvalidPrefix));
        });
        m_CleanupService.Verify(service => service.CleanupByPrefixAsync(
            It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task CleanupByPrefix_OldSuffixBody_IsRejectedAsInvalidPrefixBeforeExecution()
    {
        var request = JsonSerializer.Deserialize<CleanupByPrefixRequest>(
            """{"suffix":"_bg.png","dryRun":false}""");
        Assert.That(request, Is.Not.Null);

        AssetPrefixCleanupRequest? captured = null;
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AssetPrefixCleanupRequest, CancellationToken>((cleanupRequest, _) => captured = cleanupRequest)
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidPrefix, "A non-whitespace prefix is required."));

        var result = await m_Controller.CleanupByPrefix(request!, CancellationToken.None);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(request!.Prefix, Is.Null);
            Assert.That(objectResult.StatusCode, Is.EqualTo(400));
            Assert.That(JsonSerializer.Serialize(objectResult.Value),
                Does.Contain(AssetCleanupErrorCodes.InvalidPrefix));
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Prefix, Is.Empty);
            Assert.That(captured.Prefix, Is.Not.EqualTo("_bg.png"));
        });
        m_CleanupService.Verify(service => service.CleanupByPrefixAsync(
            It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CleanupByPrefix_InvalidPrefix_ReturnsBadRequest()
    {
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.InvalidPrefix, "A non-whitespace prefix is required."));

        var result = await m_Controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = " " }, CancellationToken.None);

        var objectResult = (ObjectResult)result;
        Assert.Multiple(() =>
        {
            Assert.That(objectResult.StatusCode, Is.EqualTo(400));
            Assert.That(JsonSerializer.Serialize(objectResult.Value),
                Does.Contain(AssetCleanupErrorCodes.InvalidPrefix));
        });
    }

    [TestCase(AssetCleanupErrorCodes.BackupBucketConflict)]
    [TestCase(AssetCleanupErrorCodes.SourceBucketConflict)]
    [TestCase(AssetCleanupErrorCodes.LifecycleConflict)]
    public async Task CleanupByPrefix_ConfigurationConflict_ReturnsConflict(string errorCode)
    {
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(errorCode, "Buckets or lifecycle rules conflict."));

        var result = await m_Controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = SourcePrefix, DryRun = false }, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task CleanupByPrefix_StorageFailure_ReturnsBadGateway()
    {
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.ListingFailed, "Listing failed."));

        var result = await m_Controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = SourcePrefix, DryRun = false }, CancellationToken.None);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(502));
    }

    [Test]
    public async Task CleanupByPrefix_StorageFailure_DoesNotLogProviderExceptionText()
    {
        m_CleanupService.Setup(service => service.CleanupByPrefixAsync(
                It.IsAny<AssetPrefixCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AssetCleanupException(
                AssetCleanupErrorCodes.ListingFailed,
                "The asset bucket listing failed; no objects were changed.",
                new AmazonS3Exception("listing failed\u001b[2J\r\nINJECTED\u000b\u000c\u0085NEL\u2028LS\u2029PS")));

        var logger = new CapturingLogger<AssetCleanupController>();
        var controller = new AssetCleanupController(m_CleanupService.Object, logger);

        var result = await controller.CleanupByPrefix(
            new CleanupByPrefixRequest { Prefix = SourcePrefix, DryRun = false }, CancellationToken.None);

        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(502));
            Assert.That(entry.Exception, Is.Null);
            Assert.That(entry.Message, Does.Contain(AssetCleanupErrorCodes.ListingFailed));
            Assert.That(entry.Message, Does.Contain("AmazonS3Exception"));
            Assert.That(entry.Message, Does.Not.Contain("INJECTED"));
            Assert.That(entry.Message.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']),
                Is.EqualTo(-1));
            Assert.That(entry.Message.Any(char.IsControl), Is.False);
        });
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }
}
