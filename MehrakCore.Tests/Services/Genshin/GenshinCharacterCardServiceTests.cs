#region

using System.Text.Json;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Services;
using MehrakCore.Services.Genshin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Mongo2Go;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Genshin;

public class GenshinCharacterCardServiceTests
{
    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData");

    private MongoDbRunner m_MongoDbRunner;
    private ImageRepository m_ImageRepository;

    [SetUp]
    public async Task Setup()
    {
        m_MongoDbRunner = MongoDbRunner.Start();

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = m_MongoDbRunner.ConnectionString,
            ["MongoDB:DatabaseName"] = "TestDb"
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var dbMock = new Mock<MongoDbService>(config, new NullLogger<MongoDbService>());

        m_ImageRepository = new ImageRepository(dbMock.Object, new NullLogger<ImageRepository>());

        foreach (var image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*",
                     SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(image);
            if (await m_ImageRepository.FileExistsAsync(fileName)) continue;

            await using var stream = File.OpenRead(image);
            await m_ImageRepository.UploadFileAsync(fileName, stream);
        }

        foreach (var image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}TestData/Genshin/Assets", "*"))
        {
            var fileName = Path.GetFileName(image).Split('.')[0];
            if (await m_ImageRepository.FileExistsAsync(fileName)) continue;

            await using var stream = File.OpenRead(image);
            await m_ImageRepository.UploadFileAsync(fileName, stream);
        }
    }

    [TearDown]
    public void TearDown()
    {
        m_MongoDbRunner.Dispose();
    }

    [Test]
    public async Task GenerateCharacterCard_MatchesGoldenImage()
    {
        // Arrange
        var characterDetail =
            JsonSerializer.Deserialize<GenshinCharacterDetail>(
                await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
        Assert.That(characterDetail, Is.Not.Null);

        var service = new GenshinCharacterCardService(m_ImageRepository, new NullLogger<GenshinCharacterCardService>());

        // Act
        var image = await service.GenerateCharacterCardAsync(characterDetail.List[0], "Test");
        using var file = new MemoryStream();
        await image.CopyToAsync(file);
        var goldenImage = await File.ReadAllBytesAsync($"{TestDataPath}/Genshin/Assets/GoldenImage.jpg");

        // Assert
        Assert.That(file.ToArray(), Is.EqualTo(goldenImage));
    }

    // To be used to generate golden image should the generation algorithm be updated
    // public async Task GenerateGoldenImage()
    // {
    //     // Arrange
    //     var characterDetail =
    //         JsonSerializer.Deserialize<GenshinCharacterDetail>(
    //             await File.ReadAllTextAsync($"{TestDataPath}/Genshin/Aether_TestData.json"));
    //     Assert.That(characterDetail, Is.Not.Null);
    //
    //     var service = new GenshinCharacterCardService(m_ImageRepository, new NullLogger<GenshinCharacterCardService>());
    //
    //     // Act
    //     var image = await service.GenerateCharacterCardAsync(characterDetail.List[0], "Test");
    //     using var file = new MemoryStream();
    //     await image.CopyToAsync(file);
    //     await File.WriteAllBytesAsync($"{TestDataPath}/Genshin/Assets/GoldenImage.jpg", file.ToArray());
    //
    //     // Assert
    //     Assert.That(image, Is.Not.Null);
    // }
}
