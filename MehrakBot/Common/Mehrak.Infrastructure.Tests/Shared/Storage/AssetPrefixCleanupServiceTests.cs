#region

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Storage;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

#endregion

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
internal sealed class AssetPrefixCleanupServiceTests
{
    private const string SourceBucket = "mehrak-assets";
    private const string BackupBucket = "mehrak-asset-backups";
    private const string AttachmentBucket = "mehrak-attachments";
    private const string PortraitBucket = "mehrak-uploads";

    private readonly Mock<IAmazonS3> m_S3 = new();
    private readonly Mock<IImageRepository> m_Repository = new();
    private readonly List<string> m_Operations = [];
    private readonly List<ListObjectsV2Request> m_SourceRequests = [];

    [SetUp]
    public void SetUp()
    {
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = [], IsTruncated = false });

        m_Repository.Setup(x => x.InvalidateCache(It.IsAny<string>()))
            .Callback<string>(key => m_Operations.Add($"cache:{key}"));
    }

    [Test]
    public async Task DryRun_ListsEveryMatchingPageWithoutAnyMutation()
    {
        SetupSourcePages(
            new ListObjectsV2Response
            {
                S3Objects = [CreateObject("genshin/a_bg.png"), CreateObject("genshin/b.png")],
                IsTruncated = true,
                NextContinuationToken = "page-2"
            },
            new ListObjectsV2Response
            {
                S3Objects = [CreateObject("genshin/c_bg.png"), CreateObject("hsr/d_bg.png")],
                IsTruncated = false
            });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/"));

        Assert.Multiple(() =>
        {
            Assert.That(result.DryRun, Is.True);
            Assert.That(result.Prefix, Is.EqualTo("genshin/"));
            Assert.That(result.MatchedCount, Is.EqualTo(3));
            Assert.That(result.DeletedCount, Is.Zero);
            Assert.That(result.FailedCount, Is.Zero);
            Assert.That(result.BackupLocation, Is.Null);
            Assert.That(result.Keys.Select(key => key.Key),
                Is.EqualTo(new[] { "genshin/a_bg.png", "genshin/b.png", "genshin/c_bg.png" }));
            Assert.That(result.Keys.All(key => key.Status == AssetPrefixCleanupStatuses.Matched), Is.True);
        });

        Assert.That(m_SourceRequests.Select(request => request.Prefix),
            Is.EqualTo(new[] { "genshin/", "genshin/" }));
        Assert.That(m_SourceRequests.Select(request => request.ContinuationToken),
            Is.EqualTo(new string?[] { null, "page-2" }));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket &&
                                                   request.Prefix == "genshin/" &&
                                                   request.ContinuationToken == "page-2"),
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoMutations();
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.Is<ListObjectsV2Request>(request => request.BucketName == BackupBucket),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task DryRun_SelectsOnlyFullKeysBeginningWithLiteralPrefix()
    {
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects =
            [
                CreateObject("genshin/arcana_1001_bg.png"),
                CreateObject("genshin/arcana_1002.png"),
                CreateObject("genshin-other/arcana_1003.png"),
                CreateObject("legacy/genshin/arcana_1004.png"),
                CreateObject("legacy/genshin/"),
                CreateObject("Genshin/arcana_1005.png")
            ],
            IsTruncated = false
        });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Prefix, Is.EqualTo("genshin/"));
            Assert.That(result.Keys.Select(key => key.Key),
                Is.EqualTo(new[] { "genshin/arcana_1001_bg.png", "genshin/arcana_1002.png" }));
        });
    }

    [Test]
    public async Task DryRun_PrefixMatchingIsCaseSensitive()
    {
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects =
            [
                CreateObject("genshin/arcana_1001.png"),
                CreateObject("Genshin/arcana_1002.png")
            ],
            IsTruncated = false
        });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("Genshin/"));

        Assert.That(result.Keys.Select(key => key.Key), Is.EqualTo(new[] { "Genshin/arcana_1002.png" }));
    }

    [Test]
    public async Task DryRun_DoesNotRequireBackupConfiguration()
    {
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects = [CreateObject("genshin/a_bg.png")],
            IsTruncated = false
        });

        var service = CreateService(backupBucket: "");
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/"));

        Assert.Multiple(() =>
        {
            Assert.That(result.MatchedCount, Is.EqualTo(1));
            Assert.That(result.BackupLocation, Is.Null);
        });
        VerifyNoMutations();
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("genshin/\u0085", false)]
    [TestCase("genshin/\u2028", false)]
    [TestCase("genshin/\u2029", false)]
    [TestCase("genshin/\r\n", true)]
    [TestCase("genshin/\u2028", true)]
    public async Task InvalidPrefix_IsRejectedBeforeListing(string? prefix, bool dryRun)
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest(prefix!, dryRun)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.InvalidPrefix));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task InvalidPrefix_ControlCharacter_IsRejectedBeforeListing()
    {
        var service = CreateService();
        var prefix = "genshin/" + (char)1;

        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest(prefix)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.InvalidPrefix));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task DryRun_MatchesPrefixLiterallyWithoutTrimmingOrNormalizing()
    {
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects =
            [
                CreateObject("genshin/a _bg.png"),
                CreateObject("genshin/a_bg.png"),
                CreateObject("genshin/a_bg.png ")
            ],
            IsTruncated = false
        });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/a "));

        Assert.Multiple(() =>
        {
            Assert.That(result.Prefix, Is.EqualTo("genshin/a "));
            Assert.That(result.Keys.Select(key => key.Key), Is.EqualTo(new[] { "genshin/a _bg.png" }));
        });
    }

    [Test]
    public async Task DryRun_ListingFailureLogsNoPrefixLineBreaksOrProviderText()
    {
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception(
                "listing failed\u001b[2J\r\nINJECTED LINE\u000b\u000c\u0085NEL\u2028LS\u2029PS")
            {
                StatusCode = HttpStatusCode.Forbidden
            });

        var logger = new CapturingLogger<AssetPrefixCleanupService>();
        var service = CreateService(logger: logger);
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/")));

        var entry = logger.Entries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.ListingFailed));
            Assert.That(exception.Message.IndexOfAny(LogLineBreaks), Is.EqualTo(-1));
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(entry.Exception, Is.Null);
            Assert.That(entry.Message, Does.Not.Contain("genshin/"));
            Assert.That(entry.Message, Does.Not.Contain("INJECTED"));
            Assert.That(entry.Message, Does.Contain("AmazonS3Exception"));
            Assert.That(entry.State["S3StatusCode"], Is.EqualTo(403));
        });
        AssertNoUnsafeLogCharacters(logger.Entries);
        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_LogsProjectionsOnlyAndKeepsStorageKeyIdentity()
    {
        const string taintedKey = "genshin/a\u001b[2J\u001b[H\r\nFAKE-KEY\u000b\u000c\u0085B\u2028C\u2029D_bg.png";
        const string taintedPrefix = "asset\u2028-cleanup";
        var tainted = CreateObject(taintedKey);
        var healthy = CreateObject("genshin/b_bg.png");
        SetupSourcePages(Page(tainted, healthy));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        SetupMetadata(healthy.Key, healthy.ETag, healthy.Size!.Value);

        var attemptedCopyKeys = new List<string>();
        m_S3.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) =>
            {
                attemptedCopyKeys.Add(request.SourceKey);
                if (request.SourceKey == taintedKey)
                {
                    throw new AmazonS3Exception("copy failed\u001b[2J\r\nPROVIDER-LEAK\u000b\u000c\u2028FAILURE")
                    {
                        StatusCode = HttpStatusCode.InternalServerError
                    };
                }
            })
            .ReturnsAsync(new CopyObjectResponse());
        m_S3.Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == healthy.Key),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        var logger = new CapturingLogger<AssetPrefixCleanupService>();
        var service = CreateService(backupPrefix: taintedPrefix, logger: logger);
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        var completion = logger.Entries.Single(entry => entry.Message.Contains("run completed"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Prefix, Is.EqualTo("genshin/"));
            Assert.That(result.DeletedCount, Is.EqualTo(1));
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Select(key => key.Key), Is.EqualTo(new[] { taintedKey, healthy.Key }));
            Assert.That(attemptedCopyKeys, Is.EqualTo(new[] { taintedKey, healthy.Key }));
            Assert.That(result.BackupLocation, Does.Contain(taintedPrefix));
            Assert.That(logger.Entries.All(entry => entry.Exception is null), Is.True);
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("PROVIDER-LEAK"));
            Assert.That(logger.Entries.SelectMany(entry => entry.State.Values.OfType<string>()),
                Has.None.Contains("PROVIDER-LEAK"));
            Assert.That(logger.Entries.All(entry => !entry.State.ContainsKey("Prefix")), Is.True);

            Assert.That(warning.Message, Does.Contain(AssetCleanupErrorCodes.CopyFailed));
            Assert.That(warning.Message, Does.Contain("AmazonS3Exception"));
            Assert.That(warning.State["Key"] as string, Does.StartWith("genshin/a").And.EndWith("_bg.png"));

            Assert.That(completion.Message, Does.Not.Contain("genshin/"));
            Assert.That(completion.State["PrefixLength"], Is.EqualTo(8));
            Assert.That(completion.State["Matched"], Is.EqualTo(2));
            Assert.That(completion.State["Deleted"], Is.EqualTo(1));
            Assert.That(completion.State["Failed"], Is.EqualTo(1));
            Assert.That(completion.State["BackupLocation"], Does.StartWith($"s3://{BackupBucket}/asset-cleanup/"));
        });
        AssertNoUnsafeLogCharacters(logger.Entries);
    }

    [Test]
    public async Task Execute_MissingBackupBucket_FailsBeforeListing()
    {
        var service = CreateService(backupBucket: "");

        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.BackupBucketUnconfigured));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(AttachmentBucket, false)]
    [TestCase(PortraitBucket, false)]
    [TestCase("MEHRAK-ATTACHMENTS", true)]
    public async Task SourceBucketMustDifferFromExcludedBuckets(string sourceBucket, bool dryRun)
    {
        var service = CreateService(sourceBucket: sourceBucket);

        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", dryRun)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.SourceBucketConflict));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoMutations();
    }

    [TestCase(SourceBucket)]
    [TestCase(AttachmentBucket)]
    [TestCase(PortraitBucket)]
    public async Task Execute_BackupBucketMustDifferFromOwnedBuckets(string backupBucket)
    {
        var service = CreateService(backupBucket: backupBucket);

        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.BackupBucketConflict));
        m_S3.Verify(x => x.ListObjectsV2Async(
            It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Execute_TruncatedListingWithoutToken_ChangesNothing()
    {
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects = [CreateObject("genshin/a_bg.png")],
            IsTruncated = true,
            NextContinuationToken = null
        });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.ListingTokenMissing));
        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_RepeatedListingToken_ChangesNothing()
    {
        SetupSourcePages(
            new ListObjectsV2Response
            {
                S3Objects = [CreateObject("genshin/a_bg.png")],
                IsTruncated = true,
                NextContinuationToken = "repeated"
            },
            new ListObjectsV2Response
            {
                S3Objects = [CreateObject("genshin/b_bg.png")],
                IsTruncated = true,
                NextContinuationToken = "repeated"
            });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.ListingTokenRepeated));
        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_ListingFailure_ChangesNothing()
    {
        var page = new ListObjectsV2Response
        {
            S3Objects = [CreateObject("genshin/a_bg.png")],
            IsTruncated = true,
            NextContinuationToken = "page-2"
        };
        var calls = 0;
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (calls++ == 0)
                    return page;

                throw new AmazonS3Exception("listing failed") { StatusCode = HttpStatusCode.InternalServerError };
            });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.ListingFailed));
        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_CancelledDuringListing_ChangesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        SetupSourcePages(new ListObjectsV2Response
        {
            S3Objects = [CreateObject("genshin/a_bg.png")],
            IsTruncated = false
        });

        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                cancellation.Cancel();
                return new ListObjectsV2Response
                {
                    S3Objects = [CreateObject("genshin/a_bg.png")],
                    IsTruncated = false
                };
            });

        var service = CreateService();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.CleanupByPrefixAsync(
                new AssetPrefixCleanupRequest("genshin/", DryRun: false), cancellation.Token));

        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_ZeroMatches_DoesNotSetUpStorage()
    {
        SetupSourcePages(new ListObjectsV2Response { S3Objects = [], IsTruncated = false });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.MatchedCount, Is.Zero);
            Assert.That(result.DeletedCount, Is.Zero);
            Assert.That(result.FailedCount, Is.Zero);
            Assert.That(result.BackupLocation, Is.Null);
            Assert.That(result.Keys, Is.Empty);
        });
        VerifyNoMutations();
    }

    [Test]
    public async Task Execute_CreatesMissingBackupBucketBeforeApplyingLifecycle()
    {
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketMissing();
        SetupMissingLifecycle();
        SetupSuccessfulCopyVerifyDelete(source);

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.That(result.DeletedCount, Is.EqualTo(1));
        m_S3.Verify(x => x.PutBucketAsync(
            It.Is<PutBucketRequest>(request => request.BucketName == BackupBucket),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(m_Operations, Is.EqualTo(new[]
        {
            "bucket",
            "lifecycle",
            $"copy:{source.Key}",
            $"delete:{source.Key}",
            $"cache:{source.Key}"
        }));
    }

    [Test]
    public async Task Execute_AppliesSevenDayLifecyclePreservingExistingRulesBeforeBackupAndDelete()
    {
        var existingRule = new LifecycleRule
        {
            Id = "ExistingRule",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration { Days = 30 }
        };
        var existingExpireRule = new LifecycleRule
        {
            Id = AssetPrefixCleanupService.ExpireRuleId,
            Status = LifecycleRuleStatus.Enabled,
            Filter = PrefixFilter("asset-cleanup/"),
            Expiration = new LifecycleRuleExpiration { Days = 7 },
            NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration { NoncurrentDays = 7 }
        };
        var existingDeleteMarkerRule = new LifecycleRule
        {
            Id = AssetPrefixCleanupService.DeleteMarkerRuleId,
            Status = LifecycleRuleStatus.Enabled,
            Filter = PrefixFilter("asset-cleanup/"),
            Expiration = new LifecycleRuleExpiration { ExpiredObjectDeleteMarker = true }
        };
        var source = CreateObject("genshin/a_bg.png", size: 42);
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();

        PutLifecycleConfigurationRequest? putRequest = null;
        m_S3.Setup(x => x.GetLifecycleConfigurationAsync(
                It.Is<GetLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetLifecycleConfigurationResponse
            {
                Configuration = new LifecycleConfiguration
                {
                    Rules = [existingRule, existingExpireRule, existingDeleteMarkerRule]
                }
            });
        m_S3.Setup(x => x.PutLifecycleConfigurationAsync(
                It.Is<PutLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .Callback<PutLifecycleConfigurationRequest, CancellationToken>((request, _) =>
            {
                putRequest = request;
                m_Operations.Add("lifecycle");
            })
            .ReturnsAsync(new PutLifecycleConfigurationResponse());

        SetupSuccessfulCopyVerifyDelete(source);

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.DeletedCount, Is.EqualTo(1));
            Assert.That(result.FailedCount, Is.Zero);
            Assert.That(result.BackupLocation, Does.StartWith($"s3://{BackupBucket}/asset-cleanup/"));
            Assert.That(result.Keys.Single().BackupKey, Does.Contain(source.Key));
        });

        Assert.That(putRequest, Is.Not.Null);
        var rules = putRequest!.Configuration.Rules;
        Assert.Multiple(() =>
        {
            Assert.That(rules.Select(rule => rule.Id), Does.Contain("ExistingRule"));
            Assert.That(rules, Has.Count.EqualTo(3));
            Assert.That(rules, Does.Not.Contain(existingExpireRule));
            Assert.That(rules, Does.Not.Contain(existingDeleteMarkerRule));

            var expireRule = rules.Single(rule => rule.Id == AssetPrefixCleanupService.ExpireRuleId);
            Assert.That(expireRule.Status, Is.EqualTo(LifecycleRuleStatus.Enabled));
            Assert.That(expireRule.Expiration.Days, Is.EqualTo(7));
            Assert.That(expireRule.NoncurrentVersionExpiration.NoncurrentDays, Is.EqualTo(7));
            Assert.That(GetRulePrefix(expireRule), Is.EqualTo("asset-cleanup/"));

            var deleteMarkerRule = rules.Single(rule => rule.Id == AssetPrefixCleanupService.DeleteMarkerRuleId);
            Assert.That(deleteMarkerRule.Status, Is.EqualTo(LifecycleRuleStatus.Enabled));
            Assert.That(deleteMarkerRule.Expiration.ExpiredObjectDeleteMarker, Is.True);
            Assert.That(GetRulePrefix(deleteMarkerRule), Is.EqualTo("asset-cleanup/"));
        });

        Assert.That(m_Operations, Is.EqualTo(new[]
        {
            "lifecycle",
            $"copy:{source.Key}",
            $"delete:{source.Key}",
            $"cache:{source.Key}"
        }));
    }

    [Test]
    public async Task Execute_LifecycleReadFailure_BlocksCopiesAndDeletes()
    {
        SetupSourcePages(Page(CreateObject("genshin/a_bg.png")));
        SetupBackupBucketExists();
        m_S3.Setup(x => x.GetLifecycleConfigurationAsync(
                It.Is<GetLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.Forbidden });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.LifecycleUnavailable));
        VerifyNoCopiesOrDeletes();
    }

    [Test]
    public async Task Execute_LifecycleWriteFailure_BlocksCopiesAndDeletes()
    {
        SetupSourcePages(Page(CreateObject("genshin/a_bg.png")));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        m_S3.Setup(x => x.PutLifecycleConfigurationAsync(
                It.IsAny<PutLifecycleConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("not supported") { StatusCode = HttpStatusCode.NotImplemented });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.LifecycleRejected));
        VerifyNoCopiesOrDeletes();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Execute_BucketWideOneDayExpiration_BlocksRun(bool nullFilter)
    {
        await AssertLifecycleConflictAsync(new LifecycleRule
        {
            Id = "BroadOneDay",
            Status = LifecycleRuleStatus.Enabled,
            Filter = nullFilter ? null : new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration { Days = 1 }
        });
    }

    [TestCase("asset")]
    [TestCase("asset-cleanup/2026/")]
    public async Task Execute_OverlappingPrefixShorterExpiration_BlocksRun(string prefix)
    {
        await AssertLifecycleConflictAsync(new LifecycleRule
        {
            Id = $"Overlap-{prefix}",
            Status = LifecycleRuleStatus.Enabled,
            Filter = PrefixFilter(prefix),
            Expiration = new LifecycleRuleExpiration { Days = 1 }
        });
    }

    [Test]
    public async Task Execute_OverlappingNoncurrentShorterExpiration_BlocksRun()
    {
        await AssertLifecycleConflictAsync(new LifecycleRule
        {
            Id = "NoncurrentOneDay",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter(),
            NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration { NoncurrentDays = 1 }
        });
    }

    [Test]
    public async Task Execute_OverlappingAbsoluteDateBeforeRetention_BlocksRun()
    {
        await AssertLifecycleConflictAsync(new LifecycleRule
        {
            Id = "EarlyAbsoluteDate",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration
            {
                Date = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)
            }
        });
    }

    [Test]
    public async Task Execute_UnprovableSizeFilter_BlocksRun()
    {
        await AssertLifecycleConflictAsync(new LifecycleRule
        {
            Id = "SizeFilter",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter { ObjectSizeGreaterThan = 1 },
            Expiration = new LifecycleRuleExpiration { Days = 1 }
        });
    }

    [Test]
    public async Task Execute_ExistingMaintenanceRuleWithDifferentPrefix_BlocksRun()
    {
        await AssertLifecycleConflictAsync(
            new LifecycleRule
            {
                Id = AssetPrefixCleanupService.ExpireRuleId,
                Status = LifecycleRuleStatus.Enabled,
                Filter = PrefixFilter("old-prefix/"),
                Expiration = new LifecycleRuleExpiration { Days = 7 },
                NoncurrentVersionExpiration = new LifecycleRuleNoncurrentVersionExpiration { NoncurrentDays = 7 }
            },
            new LifecycleRule
            {
                Id = AssetPrefixCleanupService.DeleteMarkerRuleId,
                Status = LifecycleRuleStatus.Enabled,
                Filter = PrefixFilter("old-prefix/"),
                Expiration = new LifecycleRuleExpiration { ExpiredObjectDeleteMarker = true }
            });
    }

    [Test]
    public async Task Execute_ExistingSafeRules_ArePreservedUnchanged()
    {
        var disjoint = new LifecycleRule
        {
            Id = "DisjointPrefix",
            Status = LifecycleRuleStatus.Enabled,
            Filter = PrefixFilter("other/"),
            Expiration = new LifecycleRuleExpiration { Days = 1 }
        };
        var disabled = new LifecycleRule
        {
            Id = "DisabledBroadOneDay",
            Status = LifecycleRuleStatus.Disabled,
            Filter = new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration { Days = 1 }
        };
        var broadThirtyDays = new LifecycleRule
        {
            Id = "BroadThirtyDays",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration { Days = 30 }
        };
#pragma warning disable CS0618 // Existing lifecycle configurations can still use the legacy prefix form.
        var legacyPrefix = new LifecycleRule
        {
            Id = "LegacyPrefix",
            Status = LifecycleRuleStatus.Enabled,
            Prefix = "asset-cleanup/",
            Expiration = new LifecycleRuleExpiration { Days = 30 }
        };
#pragma warning restore CS0618
        var futureAbsoluteDate = new LifecycleRule
        {
            Id = "FutureAbsoluteDate",
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter(),
            Expiration = new LifecycleRuleExpiration
            {
                Date = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
            }
        };
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();

        PutLifecycleConfigurationRequest? putRequest = null;
        SetupExistingLifecycle(
            [disjoint, disabled, broadThirtyDays, legacyPrefix, futureAbsoluteDate],
            request => putRequest = request);
        SetupSuccessfulCopyVerifyDelete(source);

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.That(result.DeletedCount, Is.EqualTo(1));
        Assert.That(putRequest, Is.Not.Null);
        var rules = putRequest!.Configuration.Rules;
        Assert.That(rules, Has.Count.EqualTo(7));
        Assert.Multiple(() =>
        {
            Assert.That(rules.Single(rule => rule.Id == disjoint.Id), Is.SameAs(disjoint));
            Assert.That(rules.Single(rule => rule.Id == disabled.Id), Is.SameAs(disabled));
            Assert.That(rules.Single(rule => rule.Id == broadThirtyDays.Id), Is.SameAs(broadThirtyDays));
            Assert.That(rules.Single(rule => rule.Id == legacyPrefix.Id), Is.SameAs(legacyPrefix));
            Assert.That(rules.Single(rule => rule.Id == futureAbsoluteDate.Id), Is.SameAs(futureAbsoluteDate));
        });
        Assert.That(m_Operations, Is.EqualTo(new[]
        {
            "lifecycle",
            $"copy:{source.Key}",
            $"delete:{source.Key}",
            $"cache:{source.Key}"
        }));
    }

    private async Task AssertLifecycleConflictAsync(params LifecycleRule[] existingRules)
    {
        SetupSourcePages(Page(CreateObject("genshin/a_bg.png")));
        SetupBackupBucketExists();
        SetupExistingLifecycle(existingRules);

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.LifecycleConflict));
        m_S3.Verify(x => x.PutLifecycleConfigurationAsync(
            It.IsAny<PutLifecycleConfigurationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoCopiesOrDeletes();
    }

    [Test]
    public async Task Execute_BackupBucketCheckFailure_BlocksEverything()
    {
        SetupSourcePages(Page(CreateObject("genshin/a_bg.png")));
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("unavailable") { StatusCode = HttpStatusCode.InternalServerError });

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<AssetCleanupException>(async () =>
            await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false)));

        Assert.That(exception!.Code, Is.EqualTo(AssetCleanupErrorCodes.BackupBucketUnavailable));
        m_S3.Verify(x => x.GetLifecycleConfigurationAsync(
            It.IsAny<GetLifecycleConfigurationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoCopiesOrDeletes();
    }

    [Test]
    public async Task Execute_CopyFailure_PreservesSource()
    {
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        m_S3.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("copy failed") { StatusCode = HttpStatusCode.InternalServerError });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.DeletedCount, Is.Zero);
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Single().Status, Is.EqualTo(AssetPrefixCleanupStatuses.Failed));
            Assert.That(result.Keys.Single().ErrorCode, Is.EqualTo(AssetCleanupErrorCodes.CopyFailed));
            Assert.That(result.Keys.Single().BackupKey, Is.Null);
        });
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_Repository.Verify(x => x.InvalidateCache(It.IsAny<string>()), Times.Never);
    }

    [TestCase("different-etag", 42L)]
    [TestCase("same-etag", 41L)]
    [TestCase(null, 42L)]
    public async Task Execute_BackupVerificationFailure_PreservesSource(string? backupETag, long backupSize)
    {
        var source = CreateObject("genshin/a_bg.png", size: 42, etag: "\"same-etag\"");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        SetupCopySucceeds(source);
        SetupMetadata(source.Key, backupETag, backupSize);

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.DeletedCount, Is.Zero);
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Single().ErrorCode,
                Is.EqualTo(AssetCleanupErrorCodes.BackupVerificationFailed));
            Assert.That(result.Keys.Single().BackupKey, Is.Not.Null);
        });
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_Repository.Verify(x => x.InvalidateCache(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Execute_CopyPreconditionFailure_ReportsSourceModified()
    {
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        m_S3.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("precondition") { StatusCode = HttpStatusCode.PreconditionFailed });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.That(result.Keys.Single().ErrorCode, Is.EqualTo(AssetCleanupErrorCodes.SourceModified));
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Execute_ConditionalDeleteUsesListedETag_AndReportsConcurrentOverwrite()
    {
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        SetupCopySucceeds(source);
        SetupMetadata(source.Key, source.ETag, source.Size!.Value);

        CopyObjectRequest? copyRequest = null;
        m_S3.Setup(x => x.CopyObjectAsync(
                It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((request, _) => copyRequest = request)
            .ReturnsAsync(new CopyObjectResponse());

        DeleteObjectRequest? deleteRequest = null;
        m_S3.Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == source.Key),
                It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((request, _) => deleteRequest = request)
            .ThrowsAsync(new AmazonS3Exception("precondition") { StatusCode = HttpStatusCode.PreconditionFailed });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(copyRequest, Is.Not.Null);
            Assert.That(copyRequest!.ETagToMatch, Is.EqualTo(source.ETag));
            Assert.That(copyRequest.MetadataDirective, Is.EqualTo(S3MetadataDirective.COPY));
            Assert.That(copyRequest.SourceBucket, Is.EqualTo(SourceBucket));
            Assert.That(copyRequest.DestinationBucket, Is.EqualTo(BackupBucket));

            Assert.That(deleteRequest, Is.Not.Null);
            Assert.That(deleteRequest!.IfMatch, Is.EqualTo(source.ETag));
            Assert.That(deleteRequest.BucketName, Is.EqualTo(SourceBucket));

            Assert.That(result.DeletedCount, Is.Zero);
            Assert.That(result.Keys.Single().ErrorCode, Is.EqualTo(AssetCleanupErrorCodes.SourceModified));
            Assert.That(result.Keys.Single().BackupKey, Is.Not.Null);
        });
        m_Repository.Verify(x => x.InvalidateCache(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Execute_PartialDeleteFailure_InvalidatesCacheOnlyForDeletedKeys()
    {
        var deleted = CreateObject("genshin/a_bg.png");
        var failed = CreateObject("genshin/b_bg.png");
        SetupSourcePages(Page(deleted, failed));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        SetupSuccessfulCopyVerifyDelete(deleted);
        SetupCopySucceeds(failed);
        SetupMetadata(failed.Key, failed.ETag, failed.Size!.Value);
        m_S3.Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == failed.Key),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("delete failed") { StatusCode = HttpStatusCode.InternalServerError });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.DeletedCount, Is.EqualTo(1));
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Single(key => key.Key == deleted.Key).Status,
                Is.EqualTo(AssetPrefixCleanupStatuses.Deleted));
            Assert.That(result.Keys.Single(key => key.Key == failed.Key).ErrorCode,
                Is.EqualTo(AssetCleanupErrorCodes.DeleteFailed));
        });
        m_Repository.Verify(x => x.InvalidateCache(deleted.Key), Times.Once);
        m_Repository.Verify(x => x.InvalidateCache(failed.Key), Times.Never);
    }

    [Test]
    public async Task Execute_DeleteNotFound_IsTreatedAsAlreadyDeletedAndInvalidatesCache()
    {
        var source = CreateObject("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();
        SetupCopySucceeds(source);
        SetupMetadata(source.Key, source.ETag, source.Size!.Value);
        m_S3.Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == source.Key),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.That(result.DeletedCount, Is.EqualTo(1));
        m_Repository.Verify(x => x.InvalidateCache(source.Key), Times.Once);
    }

    [Test]
    public async Task Execute_MissingETag_FailsKeyWithoutCopy()
    {
        var source = CreateObjectWithoutETag("genshin/a_bg.png");
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Single().ErrorCode,
                Is.EqualTo(AssetCleanupErrorCodes.SourceMetadataIncomplete));
        });
        m_S3.Verify(x => x.CopyObjectAsync(
            It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Execute_SourceLargerThanSingleCopyLimit_FailsKeyWithoutCopy()
    {
        var source = CreateObject("genshin/a_bg.png", size: AssetPrefixCleanupService.MaxSingleCopyBytes + 1);
        SetupSourcePages(Page(source));
        SetupBackupBucketExists();
        SetupMissingLifecycle();

        var service = CreateService();
        var result = await service.CleanupByPrefixAsync(new AssetPrefixCleanupRequest("genshin/", DryRun: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.FailedCount, Is.EqualTo(1));
            Assert.That(result.Keys.Single().ErrorCode, Is.EqualTo(AssetCleanupErrorCodes.SourceTooLarge));
        });
        m_S3.Verify(x => x.CopyObjectAsync(
            It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AssetPrefixCleanupService CreateService(
        string backupBucket = BackupBucket,
        string backupPrefix = AssetPrefixCleanupService.DefaultBackupPrefix,
        string sourceBucket = SourceBucket,
        ILogger<AssetPrefixCleanupService>? logger = null)
    {
        return new AssetPrefixCleanupService(
            m_S3.Object,
            m_Repository.Object,
            Options.Create(new S3StorageConfig { Bucket = sourceBucket }),
            Options.Create(new AssetCleanupConfig
            {
                BackupBucket = backupBucket,
                BackupPrefix = backupPrefix
            }),
            Options.Create(new AttachmentStorageConfig { Bucket = AttachmentBucket }),
            Options.Create(new UserPortraitStorageConfig { Bucket = PortraitBucket }),
            logger ?? NullLogger<AssetPrefixCleanupService>.Instance,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-01-02T03:04:05Z")));
    }

    private void SetupSourcePages(params ListObjectsV2Response[] pages)
    {
        var queue = new Queue<ListObjectsV2Response>(pages);
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == SourceBucket),
                It.IsAny<CancellationToken>()))
            .Callback<ListObjectsV2Request, CancellationToken>((request, _) => m_SourceRequests.Add(request))
            .ReturnsAsync(() => queue.Dequeue());
    }

    private void SetupBackupBucketExists()
    {
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response { S3Objects = [], IsTruncated = false });
    }

    private void SetupBackupBucketMissing()
    {
        m_S3.Setup(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchBucket"
            });
        m_S3.Setup(x => x.PutBucketAsync(
                It.Is<PutBucketRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .Callback<PutBucketRequest, CancellationToken>((_, _) => m_Operations.Add("bucket"))
            .ReturnsAsync(new PutBucketResponse());
    }

    private void SetupExistingLifecycle(
        IEnumerable<LifecycleRule> rules,
        Action<PutLifecycleConfigurationRequest>? capture = null)
    {
        m_S3.Setup(x => x.GetLifecycleConfigurationAsync(
                It.Is<GetLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetLifecycleConfigurationResponse
            {
                Configuration = new LifecycleConfiguration { Rules = rules.ToList() }
            });
        m_S3.Setup(x => x.PutLifecycleConfigurationAsync(
                It.Is<PutLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .Callback<PutLifecycleConfigurationRequest, CancellationToken>((request, _) =>
            {
                capture?.Invoke(request);
                m_Operations.Add("lifecycle");
            })
            .ReturnsAsync(new PutLifecycleConfigurationResponse());
    }

    private static LifecycleFilter PrefixFilter(string prefix) => new()
    {
        LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix }
    };

    private void SetupMissingLifecycle()
    {
        m_S3.Setup(x => x.GetLifecycleConfigurationAsync(
                It.Is<GetLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("missing")
            {
                StatusCode = HttpStatusCode.NotFound,
                ErrorCode = "NoSuchLifecycleConfiguration"
            });
        m_S3.Setup(x => x.PutLifecycleConfigurationAsync(
                It.Is<PutLifecycleConfigurationRequest>(request => request.BucketName == BackupBucket),
                It.IsAny<CancellationToken>()))
            .Callback<PutLifecycleConfigurationRequest, CancellationToken>((_, _) => m_Operations.Add("lifecycle"))
            .ReturnsAsync(new PutLifecycleConfigurationResponse());
    }

    private void SetupCopySucceeds(S3Object source)
    {
        m_S3.Setup(x => x.CopyObjectAsync(
                It.Is<CopyObjectRequest>(request => request.SourceKey == source.Key),
                It.IsAny<CancellationToken>()))
            .Callback<CopyObjectRequest, CancellationToken>((_, _) => m_Operations.Add($"copy:{source.Key}"))
            .ReturnsAsync(new CopyObjectResponse());
    }

    private void SetupMetadata(string sourceKey, string? etag, long size)
    {
        m_S3.Setup(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(request => request.Key.EndsWith(sourceKey, StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse
            {
                ETag = etag,
                ContentLength = size,
                HttpStatusCode = HttpStatusCode.OK
            });
    }

    private void SetupSuccessfulCopyVerifyDelete(S3Object source)
    {
        SetupCopySucceeds(source);
        SetupMetadata(source.Key, source.ETag, source.Size ?? 0);
        m_S3.Setup(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(request => request.Key == source.Key),
                It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((_, _) => m_Operations.Add($"delete:{source.Key}"))
            .ReturnsAsync(new DeleteObjectResponse());
    }

    private static ListObjectsV2Response Page(params S3Object[] objects) =>
        new() { S3Objects = [.. objects], IsTruncated = false };

    private static S3Object CreateObject(string key, long size = 10, string? etag = null) =>
        new()
        {
            Key = key,
            ETag = etag ?? $"\"{key}-etag\"",
            Size = size
        };

    private static S3Object CreateObjectWithoutETag(string key, long size = 10) =>
        new()
        {
            Key = key,
            ETag = null,
            Size = size
        };

    private static string? GetRulePrefix(LifecycleRule rule) =>
        (rule.Filter?.LifecycleFilterPredicate as LifecyclePrefixPredicate)?.Prefix;

    private void VerifyNoMutations()
    {
        m_S3.Verify(x => x.PutBucketAsync(
            It.IsAny<PutBucketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.GetLifecycleConfigurationAsync(
            It.IsAny<GetLifecycleConfigurationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.PutLifecycleConfigurationAsync(
            It.IsAny<PutLifecycleConfigurationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.CopyObjectAsync(
            It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.GetObjectMetadataAsync(
            It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_Repository.Verify(x => x.InvalidateCache(It.IsAny<string>()), Times.Never);
    }

    private void VerifyNoCopiesOrDeletes()
    {
        m_S3.Verify(x => x.CopyObjectAsync(
            It.IsAny<CopyObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.GetObjectMetadataAsync(
            It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_S3.Verify(x => x.DeleteObjectAsync(
            It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        m_Repository.Verify(x => x.InvalidateCache(It.IsAny<string>()), Times.Never);
    }

    private static readonly char[] LogLineBreaks = ['\r', '\n', '\u0085', '\u2028', '\u2029'];

    private static void AssertNoUnsafeLogCharacters(IEnumerable<CapturedLogEntry> entries)
    {
        foreach (var entry in entries)
        {
            AssertLogProjectionIsInert(entry.Message, "A rendered log message");

            foreach (var value in entry.State.Values.OfType<string>())
                AssertLogProjectionIsInert(value, "A structured log value");
        }
    }

    private static void AssertLogProjectionIsInert(string text, string description)
    {
        Assert.Multiple(() =>
        {
            Assert.That(text.IndexOfAny(LogLineBreaks), Is.EqualTo(-1),
                $"{description} must not contain a line break");
            Assert.That(text.Any(char.IsControl), Is.False,
                $"{description} must not contain a control character");
        });
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> State);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var pair in pairs)
                    values[pair.Key] = pair.Value;
            }

            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception, values));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
