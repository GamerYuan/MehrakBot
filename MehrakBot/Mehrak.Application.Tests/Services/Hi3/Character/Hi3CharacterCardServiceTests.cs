using System.Text.Json;
using System.Text.Json.Serialization;
using Mehrak.Application.Services.Hi3.Character;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;
using Moq;

namespace Mehrak.Application.Tests.Services.Hi3.Character;

internal class Hi3CharacterCardServiceTests
{

    private static string TestDataPath => Path.Combine(AppContext.BaseDirectory, "TestData", "Hi3");

    private Hi3CharacterCardService m_CharacterCardService;

    private const string TestNickName = "Test";
    private const string TestUid = "800000000";
    private const ulong TestUserId = 1;

    [SetUp]
    public async Task Setup()
    {
        m_CharacterCardService = new Hi3CharacterCardService(
            MongoTestHelper.Instance.ImageRepository,
            Mock.Of<ILogger<Hi3CharacterCardService>>());
        await m_CharacterCardService.InitializeAsync();
    }

    private static GameProfileDto GetTestUserGameData()
    {
        return new GameProfileDto
        {
            GameUid = TestUid,
            Nickname = TestNickName,
            Level = 88
        };
    }

    private class TestCardGenerationContext<T> : ICardGenerationContext<T, Hi3Server>
    {
        public ulong UserId { get; }
        public T Data { get; }
        public Hi3Server Server { get; }
        public GameProfileDto GameProfile { get; }

        public TestCardGenerationContext(ulong userId, T data, Hi3Server server, GameProfileDto gameProfile)
        {
            UserId = userId;
            Data = data;
            Server = server;
            GameProfile = gameProfile;
        }
    }

    [Test]
    [TestCase("Character_TestData_1.json", "Character_GoldenImage_1.jpg")]
    [TestCase("Character_TestData_2.json", "Character_GoldenImage_2.jpg")]
    public async Task GenerateGoldenImage(string testDataFileName, string goldenImageFileName)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var characterDetail =
            JsonSerializer.Deserialize<Hi3CharacterDetail>(await
                File.ReadAllTextAsync(Path.Combine(TestDataPath, testDataFileName)), options);
        Assert.That(characterDetail, Is.Not.Null);

        GameProfileDto profile = GetTestUserGameData();

        var image = await m_CharacterCardService.GetCardAsync(new
            TestCardGenerationContext<Hi3CharacterDetail>(TestUserId,
            characterDetail, Hi3Server.SEA, profile));
        using var stream = new MemoryStream();
        await image.CopyToAsync(stream);
        await File.WriteAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets",
            "Hi3", "TestAssets", goldenImageFileName), stream.ToArray());

        Assert.That(image, Is.Not.Null);
    }

}
