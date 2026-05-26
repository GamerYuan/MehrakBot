#region

using Amazon.S3;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.LocalStack;

#endregion

namespace Mehrak.Application.Tests;

public sealed class S3TestHelper : IDisposable
{
    public static S3TestHelper Instance { get; private set; } = null!;

    private readonly LocalStackContainer m_LocalStackContainer;
    private readonly AmazonS3Client m_Client;
    private readonly IOptions<S3StorageConfig> m_Options;

    public IImageRepository ImageRepository =>
        new ImageRepository(m_Client, m_Options, Mock.Of<ILogger<ImageRepository>>(), new MemoryCache(new MemoryCacheOptions()));

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
