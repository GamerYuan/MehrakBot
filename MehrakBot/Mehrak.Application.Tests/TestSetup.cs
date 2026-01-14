namespace Mehrak.Application.Tests;

[SetUpFixture]
public class TestSetup
{
    private S3TestHelper m_DbTestHelper;

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_DbTestHelper = new S3TestHelper();

        foreach (var image in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Assets"),
                     "*.png", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(image);
            await using var stream = File.OpenRead(image);
            await S3TestHelper.Instance.ImageRepository.UploadFileAsync(fileName, stream);
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_DbTestHelper.Dispose();
    }
}
