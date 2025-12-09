namespace Mehrak.Application.Tests;

[SetUpFixture]
public class TestSetup
{
    private DbTestHelper m_MongoTestHelper;

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_MongoTestHelper = new DbTestHelper();

        foreach (var image in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"),
                     "*.png", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(image);
            await using var stream = File.OpenRead(image);
            await DbTestHelper.Instance.ImageRepository.UploadFileAsync(fileName, stream);
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_MongoTestHelper.Dispose();
    }
}
