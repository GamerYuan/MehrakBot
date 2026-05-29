namespace Mehrak.Application.Tests;

using Mehrak.Domain.Image.Models;

[SetUpFixture]
public class TestSetup
{
    private S3TestHelper m_DbTestHelper;

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_DbTestHelper = new S3TestHelper();

        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");

        foreach (var image in Directory.EnumerateFiles(assetsRoot, "*.png", SearchOption.AllDirectories))
        {
            var key = GetAssetKey(assetsRoot, image);
            await using var stream = File.OpenRead(image);
            await S3TestHelper.Instance.ImageRepository.UploadFileAsync(key, stream, FileNameFormat.PngContentType);
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_DbTestHelper.Dispose();
    }

    private static string GetAssetKey(string assetsRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(assetsRoot, fullPath)
            .Replace("TestAssets\\", "")
            .Replace("TestAssets/", "");
        var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/');
        var fileName = Path.GetFileName(relative);
        return string.IsNullOrEmpty(dir) ? fileName : $"{dir.ToLowerInvariant()}/{fileName}";
    }
}
