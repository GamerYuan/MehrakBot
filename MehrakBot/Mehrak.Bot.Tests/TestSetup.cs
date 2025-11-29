#region

using Mehrak.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace Mehrak.Bot.Tests;

[SetUpFixture]
public class TestSetup
{
    private MongoTestHelper m_MongoTestHelper;

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_MongoTestHelper = new MongoTestHelper();

        var imageRepository =
            new ImageRepository(m_MongoTestHelper.MongoDbService, NullLogger<ImageRepository>.Instance);

        foreach (var image in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"),
                     "*.png", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(image);
            await using FileStream stream = File.OpenRead(image);
            await imageRepository.UploadFileAsync(fileName, stream);
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
    }
}
