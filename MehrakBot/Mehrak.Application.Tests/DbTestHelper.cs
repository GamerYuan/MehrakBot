#region

using Amazon.S3;
using Mehrak.Domain.Repositories;
using Mehrak.Infrastructure.Config;
using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.LocalStack;

#endregion

namespace Mehrak.Application.Tests;

public sealed class DbTestHelper : IDisposable
{
    public static DbTestHelper Instance { get; private set; } = null!;

    private readonly LocalStackContainer m_LocalStackContainer;

    public IImageRepository ImageRepository { get; }

    private ulong m_TestUserId = 1_000_000_000;
    private readonly AmazonS3Client m_Client;

    public DbTestHelper()
    {
        Instance = this;

        m_LocalStackContainer = new LocalStackBuilder()
            .WithImage("localstack/localstack:latest")
            .WithPortBinding(4566, true)
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

        Mock<IOptions<S3StorageConfig>> options = new();
        options.Setup(x => x.Value).Returns(storageConfig);

        ImageRepository = new ImageRepository(m_Client, options.Object, Mock.Of<ILogger<ImageRepository>>());
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
