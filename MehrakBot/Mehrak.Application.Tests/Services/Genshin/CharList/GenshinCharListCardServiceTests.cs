#region

using System.Text.Json;
using Mehrak.Application.Services.Common.Types;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;

#endregion

namespace Mehrak.Application.Tests.Services.Genshin.CharList;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharListCardServiceTests
{
    private GenshinCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public void Setup()
    {
        m_Service = new GenshinCharListCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>());
    }

    [Test]
    [TestCase("CharList_TestData_1.json")]
    [TestCase("CharList_TestData_2.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        CharacterListData? testData =
            await JsonSerializer.DeserializeAsync<CharacterListData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        GameProfileDto userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, testData!.List!, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        Stream stream = await m_Service.GetCardAsync(cardContext);
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var bytes = memoryStream.ToArray();

        // Save generated image to output folder for comparison
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        Directory.CreateDirectory(outputDirectory);
        var outputImagePath = Path.Combine(outputDirectory,
            $"GenshinCharList_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Generated.jpg");
        await File.WriteAllBytesAsync(outputImagePath, bytes);

        // Save golden image to output folder for comparison
        var outputGoldenImagePath = Path.Combine(outputDirectory,
            $"GenshinCharList_Data{Path.GetFileNameWithoutExtension(testDataFileName).Last()}_Golden.jpg");
        await File.WriteAllBytesAsync(outputGoldenImagePath, goldenImage);

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes, Is.EqualTo(goldenImage));
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

    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     var testData1 = await JsonSerializer.DeserializeAsync<CharacterListData>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //       "CharList_TestData_1.json")));
    //     var testData2 = await JsonSerializer.DeserializeAsync<CharacterListData>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //     "CharList_TestData_2.json")));
    //
    //     var userGameData = GetTestUserGameData();
    //
    //     var image1 = await m_Service.GetCardAsync(
    //         new TestCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, testData1!.List!, Server.Asia, userGameData));
    //     var image2 = await m_Service.GetCardAsync(
    //         new TestCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, testData2!.List!, Server.Asia, userGameData));
    //
    //     Assert.Multiple(() =>
    //     {
    // Assert.That(image1, Is.Not.Null);
    //         Assert.That(image2, Is.Not.Null);
    //  });
    //
    //     await using var fileStream1 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "CharList_GoldenImage_1.jpg"));
    //     await using var fileStream2 = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
    //         "TestAssets", "CharList_GoldenImage_2.jpg"));
    //
    //     await image1.CopyToAsync(fileStream1);
    //     await image2.CopyToAsync(fileStream2);
    // }
}
