#region

using System.Text.Json;
using Mehrak.Application.Genshin.CharList;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Services.Types;
using Mehrak.Application.Tests.TestUtils;
using Mehrak.Domain.Image;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Models;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Tests.Genshin.CharList;

[Parallelizable(ParallelScope.Fixtures)]
public class GenshinCharListCardServiceTests
{
    private GenshinCharListCardService m_Service;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    private static readonly string TestDataPath = Path.Combine(AppContext.BaseDirectory, "TestData");

    [SetUp]
    public async Task Setup()
    {
        m_Service = new GenshinCharListCardService(
            S3TestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<GenshinCharListCardService>>(),
            Mock.Of<IApplicationMetrics>());

        await m_Service.InitializeAsync();
    }

    [Test]
    [TestCase("CharList_TestData_1.json")]
    [TestCase("CharList_TestData_2.json")]
    [TestCase("CharList_TestData_3.json")]
    public async Task GetCharListCardAsync_AllTestData_MatchesGoldenImage(string testDataFileName)
    {
        var testData =
            await JsonSerializer.DeserializeAsync<CharacterListData>(
                File.OpenRead(Path.Combine(TestDataPath, "Genshin", testDataFileName)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var goldenImage =
            await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
                "TestAssets", testDataFileName.Replace("TestData", "GoldenImage").Replace(".json", ".jpg")));

        var userGameData = GetTestUserGameData();

        var cardContext = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, testData!.List!, userGameData);
        cardContext.SetParameter("server", Server.Asia);

        var stream = await m_Service.GetCardAsync(cardContext);
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
        using var goldenStream = new MemoryStream(goldenImage);
        Assert.That(memoryStream, IsImage.IdenticalTo(goldenStream));
    }

    [Test]
    public async Task GetCardAsync_StartsAvatarLoadBeforeWeaponCompletes_AndKeepsLayoutDimensions()
    {
        using var image = new Image<Rgba32>(150, 150);
        using var encoded = new MemoryStream();
        await image.SaveAsPngAsync(encoded);
        var bytes = encoded.ToArray();
        var weaponStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var avatarStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWeapon = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IImageRepository>();
        repository.Setup(x => x.DownloadFileToStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string name, CancellationToken token) =>
            {
                if (name.StartsWith("genshin/weapon_", StringComparison.Ordinal))
                {
                    weaponStarted.TrySetResult();
                    await releaseWeapon.Task.WaitAsync(token);
                }
                if (name.StartsWith("genshin/avatar_", StringComparison.Ordinal)) avatarStarted.TrySetResult();
                return new MemoryStream(bytes, writable: false);
            });
        var service = new GenshinCharListCardService(repository.Object,
            Mock.Of<ILogger<GenshinCharListCardService>>(), Mock.Of<IApplicationMetrics>());
        await service.InitializeAsync();
        var data = new[]
        {
            new GenshinBasicCharacterData
            {
                Id = 1, Name = "Test", Icon = "avatar", Level = 90, Rarity = 5, Element = "Pyro",
                Weapon = new Weapon { Id = 1, Name = "Weapon", Icon = "weapon", Level = 20, Rarity = 3, AffixLevel = 1 }
            }
        };
        var context = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, data, GetTestUserGameData());
        var generation = service.GetCardAsync(context);
        try
        {
            await weaponStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await avatarStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(generation.IsCompleted, Is.False, "Rendering must still await the weapon.");
        }
        finally
        {
            releaseWeapon.TrySetResult();
            using var result = await generation;
            using var output = await Image.LoadAsync(result);
            Assert.That(output.Size, Is.EqualTo(new Size(430, 590)));
        }
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
    [TestCase("CharList_TestData_1.json", "CharList_GoldenImage_1.jpg")]
    [TestCase("CharList_TestData_2.json", "CharList_GoldenImage_2.jpg")]
    [TestCase("CharList_TestData_3.json", "CharList_GoldenImage_3.jpg")]
    public async Task GenerateGoldenImage(string testFilePath, string goldenImage)
    {
        var testData = await JsonSerializer.DeserializeAsync<CharacterListData>(
            File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Genshin",
          testFilePath)));
        Assert.That(testData, Is.Not.Null, "Test data should not be null");

        var userGameData = GetTestUserGameData();
        var cardContext = new BaseCardGenerationContext<IEnumerable<GenshinBasicCharacterData>>(TestUserId, testData!.List!, userGameData);
        cardContext.SetParameter("server", Server.Asia);
        var stream = await m_Service.GetCardAsync(cardContext);

        await using var fileStream = File.Create(Path.Combine(AppContext.BaseDirectory, "Assets", "Genshin",
            "TestAssets", goldenImage));

        await stream.CopyToAsync(fileStream);
        await fileStream.FlushAsync();
        fileStream.Close();
    }

}
