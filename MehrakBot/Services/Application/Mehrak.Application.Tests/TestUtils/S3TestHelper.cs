#region

using Amazon.S3;
using Mehrak.Domain.Image;
using Mehrak.Infrastructure.Shared.Config;
using Mehrak.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.LocalStack;

#endregion

namespace Mehrak.Application.Tests.TestUtils;

public sealed class S3TestHelper : IDisposable
{
    public static S3TestHelper Instance { get; private set; } = null!;

    private readonly LocalStackContainer m_LocalStackContainer;
    private readonly AmazonS3Client m_Client;
    private readonly IOptions<S3StorageConfig> m_Options;
    private readonly IOptions<AttachmentStorageConfig> m_AttachmentOptions;

    public IImageRepository ImageRepository =>
        new ImageRepository(m_Client, m_Options, Mock.Of<ILogger<ImageRepository>>(), new MemoryCache(new MemoryCacheOptions()));

    public AmazonS3Client S3Client => m_Client;

    public string BucketName => m_Options.Value.Bucket;

    public IOptions<S3StorageConfig> ImageStorageOptions => m_Options;

    public IOptions<AttachmentStorageConfig> AttachmentStorageOptions => m_AttachmentOptions;

    /// <summary>
    /// Creates an image repository sharing this run's S3 client with a caller-owned
    /// existence cache, so a benchmark run can reuse exactly one repository instance.
    /// </summary>
    public ImageRepository CreateImageRepository(IMemoryCache existsCache) =>
        new(m_Client, m_Options, NullLogger<ImageRepository>.Instance, existsCache);

    /// <summary>
    /// Creates the real attachment storage service against the local S3 bucket.
    /// Logging is disabled so no attachment names linger in test logs.
    /// </summary>
    public AttachmentStorageService CreateAttachmentStorage() =>
        new(m_Client, m_AttachmentOptions, NullLogger<AttachmentStorageService>.Instance);

    private ulong m_TestUserId = 1_000_000_000;

    public S3TestHelper()
    {
        Instance = this;

        m_LocalStackContainer = new LocalStackBuilder("localstack/localstack:latest")
            .WithEnvironment("LOCALSTACK_AUTH_TOKEN", Environment.GetEnvironmentVariable("LOCALSTACK_AUTH_TOKEN") ?? string.Empty)
            .Build();

        m_LocalStackContainer.StartAsync().GetAwaiter().GetResult();

        var config = new AmazonS3Config
        {
            ServiceURL = m_LocalStackContainer.GetConnectionString()
        };
        m_Client = new AmazonS3Client("test", "test", config);

        var putBucketRequest = new Amazon.S3.Model.PutBucketRequest
        {
            BucketName = "test-bucket",
            UseClientRegion = true
        };

        m_Client.PutBucketAsync(putBucketRequest).GetAwaiter().GetResult();

        var storageConfig = new S3StorageConfig()
        {
            AccessKey = "test",
            SecretKey = "test",
            ServiceURL = config.ServiceURL,
            Bucket = "test-bucket",
            Region = "test-region",
            ForcePathStyle = true
        };

        Mock<IOptions<S3StorageConfig>> optionsMock = new();
        optionsMock.Setup(x => x.Value).Returns(storageConfig);
        m_Options = optionsMock.Object;

        Mock<IOptions<AttachmentStorageConfig>> attachmentOptionsMock = new();
        attachmentOptionsMock.Setup(x => x.Value).Returns(new AttachmentStorageConfig
        {
            Bucket = "test-bucket"
        });
        m_AttachmentOptions = attachmentOptionsMock.Object;
    }

    public ulong GetUniqueUserId()
    {
        return Interlocked.Increment(ref m_TestUserId);
    }

    public void Dispose()
    {
        m_Client.Dispose();
        m_LocalStackContainer.DisposeAsync().GetAwaiter().GetResult();
    }
}
