#region

using System.Text.Json;
using Mehrak.Application.Services.Genshin.CharList;
using Mehrak.GameApi.Common.ApiResponseTypes;
using Mehrak.GameApi.Genshin.Types;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Services.Commands.Genshin.CharList;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Commands.Genshin;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharListCardServiceTests
{
    private ImageRepository m_ImageRepository;
    private GenshinCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");


    [SetUp]
    public void Setup()
    {
        m_ImageRepository =
            new ImageRepository(MongoTestHelper.Instance.MongoDbService, NullLogger<ImageRepository>.Instance);

        m_Service = new GenshinCharListCardService(m_ImageRepository, NullLogger<GenshinCharListCardService>.Instance);
    }

    [Test]
    [TestCase("CharList_TestData_1.json")]
    [TestCase("CharList_TestData_2.json")]
    public async Task GetTheaterCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<CharacterListData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var stream =
            await m_Service.GetCharListCardAsync(userGameData, testData!.List!);
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

    private static UserGameData GetTestUserGameData()
    {
        return new UserGameData
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 60,
            Region = "os_asia",
            RegionName = "Asia"
        };
    }

    // [Test]
    // public async Task GenerateGoldenImage()
    // {
    //     var testData1 = await JsonSerializer.DeserializeAsync<CharacterListData>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //             "CharList_TestData_1.json")));
    //     var testData2 = await JsonSerializer.DeserializeAsync<CharacterListData>(
    //         File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
    //             "CharList_TestData_2.json")));
    //
    //     var image1 =
    //         await m_Service.GetCharListCardAsync(GetTestUserGameData(), testData1!.List!);
    //     var image2 =
    //         await m_Service.GetCharListCardAsync(GetTestUserGameData(), testData2!.List!);
    //
    //     Assert.Multiple(() =>
    //     {
    //         Assert.That(image1, Is.Not.Null);
    //         Assert.That(image2, Is.Not.Null);
    //     });
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
