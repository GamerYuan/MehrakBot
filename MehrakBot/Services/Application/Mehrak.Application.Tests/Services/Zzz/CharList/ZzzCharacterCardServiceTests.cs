#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Zzz.CharList;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Zzz.CharList;

[Parallelizable(ParallelScope.Fixtures)]
public class ZzzCharListCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Zzz");

    private ZzzCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "1300000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_Service = new ZzzCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<Application.Services.Abstractions.IApplicationMetrics>(),
            Mock.Of<ILogger<ZzzCharListCardService>>());
        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("CharList_TestData_1.json", "BangbooList_TestData_1.json", "CharList_GoldenImage_1.jpg", "CharList_1")]
    public async Task GenerateCharacterCardAsync_TestData_ShouldMatchGoldenImage(string charTestDataFile, string bangbooTestDataFile,
        string goldenImageFileName, string testName)
    {
        var characterDetail =
            JsonSerializer.Deserialize<ZzzBasicAvatarResponse>(
                await File.ReadAllTextAsync(Path.Combine(TestDataPath, charTestDataFile)));
        var bangbooDetail =
            JsonSerializer.Deserialize<ZzzBuddyResponse>(
                await File.ReadAllTextAsync(Path.Combine(TestDataPath, bangbooTestDataFile)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(characterDetail, Is.Not.Null);
            Assert.That(bangbooDetail, Is.Not.Null);
        }

        var goldenImage = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz",
            "TestAssets", goldenImageFileName));

        var profile = GetTestUserGameData();

        var cardContext = new
            BaseCardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>(
                TestUserId, (characterDetail.AvatarList, bangbooDetail.List), profile);
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
        var outputImagePath = Path.Combine(outputDirectory, $"{testName}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, generatedImageBytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory, $"{testName}_Golden.jpg");
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


    [Explicit]
    [Test]
    [TestCase("CharList_TestData_1.json", "BangbooList_TestData_1.json", "CharList_GoldenImage_1.jpg")]
    [TestCase("CharList_TestData_2.json", "BangbooList_TestData_2.json", "CharList_GoldenImage_2.jpg")]
    [TestCase("CharList_TestData_3.json", "BangbooList_TestData_3.json", "CharList_GoldenImage_3.jpg")]
    public async Task GenerateGoldenImage(string charTestDataFile, string bangbooTestDataFile,
        string goldenImageFileName)
    {
        var characterDetail =
            JsonSerializer.Deserialize<ZzzBasicAvatarResponse>(
                await File.ReadAllTextAsync(Path.Combine(TestDataPath, charTestDataFile)));
        var bangbooDetail =
            JsonSerializer.Deserialize<ZzzBuddyResponse>(
                await File.ReadAllTextAsync(Path.Combine(TestDataPath, bangbooTestDataFile)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(characterDetail, Is.Not.Null);
            Assert.That(bangbooDetail, Is.Not.Null);
        }

        var profile = GetTestUserGameData();

        var cardContext = new
            BaseCardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>(
                TestUserId, (characterDetail.AvatarList, bangbooDetail.List), profile);

        var image = await m_Service.GetCardAsync(cardContext);

        var fileStream = File.OpenWrite(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Zzz", "TestAssets",
        goldenImageFileName));
        await image.CopyToAsync(fileStream);
        await fileStream.FlushAsync();

        Assert.That(image, Is.Not.Null);
    }
}
