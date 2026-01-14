#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Zzz.Assault;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.Assault;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzAssaultCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ZzzAssaultCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new ZzzAssaultCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<ZzzAssaultCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("Da_TestData_1.json")]
    [TestCase("Da_TestData_2.json")]
    [TestCase("Da_TestData_3.json")]
    public async Task GetAssaultCardAsync_TestData_ShouldMatchGoldenImage(string testData)
    {
        var assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(
            await File.ReadAllTextAsync(Path.Combine(TestDataPath, testData)));
        Assert.That(assaultData, Is.Not.Null);

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets",
            $"{Path.GetFileNameWithoutExtension(testData).Replace("TestData", "GoldenImage")}.jpg"));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<ZzzAssaultData>(TestUserId, assaultData, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        var image = await m_Service.GetCardAsync(cardContext);
        Assert.That(image, Is.Not.Null);

        MemoryStream memoryStream = new();
        await image.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        var generatedImageBytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"ZzzAssault_Data{Path.GetFileNameWithoutExtension(testData).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(generatedImageBytes, Is.EqualTo(goldenImage), "Generated image should match the golden image");
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60
        };
    }

    /*
    [Test]
    [TestCase("Da_TestData_1.json", "Da_GoldenImage_1.jpg")]
    [TestCase("Da_TestData_2.json", "Da_GoldenImage_2.jpg")]
    [TestCase("Da_TestData_3.json", "Da_GoldenImage_3.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        ZzzAssaultData? assaultData = JsonSerializer.Deserialize<ZzzAssaultData>(await
            File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)));
        Assert.That(assaultData, Is.Not.Null);

        GameProfileDto userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<ZzzAssaultData>(TestUserId, assaultData, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        Stream image = await m_Service.GetCardAsync(cardContext);

        FileStream fileStream = File.OpenWrite(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets", goldenImageFileName));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }
    */
}
