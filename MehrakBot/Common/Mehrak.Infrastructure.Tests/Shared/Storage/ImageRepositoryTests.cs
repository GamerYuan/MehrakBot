using Amazon.S3;
using Amazon.S3.Model;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Mehrak.Infrastructure.Tests.Shared.Storage;

[Parallelizable(ParallelScope.All)]
public class ImageRepositoryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ListFilesAsync_EmptyListing_ReturnsEmptyList(bool nullObjects)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = nullObjects ? null : [],
                IsTruncated = null
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);
        using var cts = new CancellationTokenSource();

        var keys = await repository.ListFilesAsync("zzz/portrait_1191_", cts.Token);

        Assert.That(keys, Is.Empty);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.BucketName == "test-bucket" && r.Prefix == "zzz/portrait_1191_" &&
            r.ContinuationToken == null), cts.Token), Times.Once);
    }

    [Test]
    public async Task ListFilesAsync_NullIntermediatePage_PreservesKeysAndContinuesPagination()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.SetupSequence(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "zzz/portrait_1371_1.png" }],
                IsTruncated = true,
                NextContinuationToken = "page-2"
            })
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = null,
                IsTruncated = true,
                NextContinuationToken = "page-3"
            })
            .ReturnsAsync(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "zzz/portrait_1371_2.png" }],
                IsTruncated = false
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = CreateRepository(s3.Object, cache);

        var keys = await repository.ListFilesAsync("zzz/portrait_1371_");

        Assert.That(keys, Is.EqualTo(new[] { "zzz/portrait_1371_1.png", "zzz/portrait_1371_2.png" }));
        foreach (var key in keys)
            Assert.That(await repository.FileExistsAsync(key), Is.True);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.ContinuationToken == "page-2"), It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(x => x.ListObjectsV2Async(It.Is<ListObjectsV2Request>(r =>
            r.ContinuationToken == "page-3"), It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ImageRepository CreateRepository(IAmazonS3 s3, IMemoryCache cache) =>
        new(s3, Options.Create(new S3StorageConfig { Bucket = "test-bucket" }),
            NullLogger<ImageRepository>.Instance, cache);
}
