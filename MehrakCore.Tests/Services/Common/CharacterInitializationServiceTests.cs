#region

using System.Text.Json;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Common;
using MehrakCore.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class CharacterInitializationServiceTests
{
    private CharacterRepository m_CharacterRepository = null!;
    private CharacterInitializationService m_InitializationService = null!;
    private string m_TestAssetsPath = null!;

    [SetUp]
    public void Setup()
    {
        m_CharacterRepository = new CharacterRepository(
            MongoTestHelper.Instance.MongoDbService,
            NullLogger<CharacterRepository>.Instance);

        // Create a temporary test assets directory
        m_TestAssetsPath = Path.Combine(Path.GetTempPath(), $"test_assets_{Guid.NewGuid()}");
        Directory.CreateDirectory(m_TestAssetsPath);

        // We'll need to modify the service to accept a custom assets path for testing
        m_InitializationService = new CharacterInitializationService(
            m_CharacterRepository,
            NullLogger<CharacterInitializationService>.Instance,
            m_TestAssetsPath);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test assets directory
        if (Directory.Exists(m_TestAssetsPath))
        {
            Directory.Delete(m_TestAssetsPath, true);
        }
    }

    [Test]
    public async Task UpsertCharactersAsync_NewGame_CreatesNewEntry()
    {
        // Arrange
        var characterModel = new CharacterModel
        {
            Game = GameName.HonkaiStarRail,
            Characters = new List<string> { "Kafka", "Blade", "Silver Wolf" }
        };

        // Act
        await m_CharacterRepository.UpsertCharactersAsync(characterModel);

        // Assert
        var retrievedModel = await m_CharacterRepository.GetCharacterModelAsync(GameName.HonkaiStarRail);
        Assert.That(retrievedModel, Is.Not.Null);
        Assert.That(retrievedModel!.Game, Is.EqualTo(GameName.HonkaiStarRail));
        Assert.That(retrievedModel.Characters, Is.EqualTo(characterModel.Characters));
    }

    [Test]
    public async Task UpsertCharactersAsync_ExistingGame_UpdatesEntry()
    {
        // Arrange - Create initial entry
        var initialModel = new CharacterModel
        {
            Game = GameName.HonkaiStarRail,
            Characters = new List<string> { "Kafka", "Blade" }
        };
        await m_CharacterRepository.UpsertCharactersAsync(initialModel);

        // Act - Update with more characters
        var updatedModel = new CharacterModel
        {
            Game = GameName.HonkaiStarRail,
            Characters = new List<string> { "Kafka", "Blade", "Silver Wolf", "Seele" }
        };
        await m_CharacterRepository.UpsertCharactersAsync(updatedModel);

        // Assert
        var retrievedModel = await m_CharacterRepository.GetCharacterModelAsync(GameName.HonkaiStarRail);
        Assert.That(retrievedModel, Is.Not.Null);
        Assert.That(retrievedModel!.Characters, Has.Count.EqualTo(4));
        Assert.That(retrievedModel.Characters, Contains.Item("Silver Wolf"));
        Assert.That(retrievedModel.Characters, Contains.Item("Seele"));
    }

    [Test]
    public async Task GetCharacterModelAsync_NonExistentGame_ReturnsNull()
    {
        // Act
        var result = await m_CharacterRepository.GetCharacterModelAsync(GameName.ZenlessZoneZero);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetCharactersAsync_NonExistentGame_ReturnsEmptyList()
    {
        // Act
        var result = await m_CharacterRepository.GetCharactersAsync(GameName.ZenlessZoneZero);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CharacterJsonModel_GetGameName_ValidGame_ReturnsCorrectEnum()
    {
        // Arrange
        var jsonModel = new CharacterJsonModel
        {
            Game = "HonkaiStarRail",
            Characters = new List<string> { "Kafka" }
        };

        // Act
        var gameName = jsonModel.GetGameName();

        // Assert
        Assert.That(gameName, Is.EqualTo(GameName.HonkaiStarRail));
    }

    [Test]
    public void CharacterJsonModel_GetGameName_InvalidGame_ThrowsArgumentException()
    {
        // Arrange
        var jsonModel = new CharacterJsonModel
        {
            Game = "InvalidGame",
            Characters = new List<string> { "Character" }
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => jsonModel.GetGameName());
    }

    [Test]
    public void CharacterJsonModel_ToCharacterModel_ConvertsCorrectly()
    {
        // Arrange
        var jsonModel = new CharacterJsonModel
        {
            Game = "HonkaiStarRail",
            Characters = new List<string> { "Kafka", "Blade" }
        };

        // Act
        var characterModel = jsonModel.ToCharacterModel();

        // Assert
        Assert.That(characterModel.Game, Is.EqualTo(GameName.HonkaiStarRail));
        Assert.That(characterModel.Characters, Is.EqualTo(jsonModel.Characters));
    }

    [Test]
    public async Task StartAsync_WithValidJsonFile_UpdatesDatabase()
    {
        // Arrange
        var testCharacters = new CharacterJsonModel
        {
            Game = "Genshin", // Use different game to avoid conflicts
            Characters = new List<string> { "Diluc", "Venti", "Zhongli" }
        };
        CreateTestCharacterJsonFile("genshin_characters.json", testCharacters);

        // Act
        await m_InitializationService.StartAsync(CancellationToken.None);

        // Assert
        var retrievedCharacters = await m_CharacterRepository.GetCharactersAsync(GameName.Genshin);
        Assert.That(retrievedCharacters, Has.Count.EqualTo(3));
        Assert.That(retrievedCharacters, Contains.Item("Diluc"));
        Assert.That(retrievedCharacters, Contains.Item("Venti"));
        Assert.That(retrievedCharacters, Contains.Item("Zhongli"));
    }

    [Test]
    public async Task StartAsync_WithExistingData_MergesCorrectly()
    {
        // Arrange - Add existing data to database
        var existingModel = new CharacterModel
        {
            Game = GameName.ZenlessZoneZero, // Use different game
            Characters = new List<string> { "Belle", "ManuallyAdded" }
        };
        await m_CharacterRepository.UpsertCharactersAsync(existingModel);

        // Create JSON file with additional characters
        var testCharacters = new CharacterJsonModel
        {
            Game = "ZenlessZoneZero",
            Characters = new List<string> { "Belle", "Wise", "Nicole" }
        };
        CreateTestCharacterJsonFile("zzz_characters.json", testCharacters);

        // Act
        await m_InitializationService.StartAsync(CancellationToken.None);

        // Assert
        var retrievedCharacters = await m_CharacterRepository.GetCharactersAsync(GameName.ZenlessZoneZero);
        Assert.That(retrievedCharacters, Has.Count.EqualTo(4)); // 3 from JSON + 1 manually added
        Assert.That(retrievedCharacters, Contains.Item("Belle"));
        Assert.That(retrievedCharacters, Contains.Item("Wise"));
        Assert.That(retrievedCharacters, Contains.Item("Nicole"));
        Assert.That(retrievedCharacters, Contains.Item("ManuallyAdded"));
    }

    [Test]
    public async Task StartAsync_NoMissingCharacters_DoesNotUpdate()
    {
        // Arrange - Add existing data that matches JSON
        var existingModel = new CharacterModel
        {
            Game = GameName.HonkaiImpact3, // Use different game
            Characters = new List<string> { "Kiana", "Mei" }
        };
        await m_CharacterRepository.UpsertCharactersAsync(existingModel);

        // Create JSON file with same characters
        var testCharacters = new CharacterJsonModel
        {
            Game = "HonkaiImpact3",
            Characters = new List<string> { "Kiana", "Mei" }
        };
        CreateTestCharacterJsonFile("hi3_characters.json", testCharacters);

        // Act
        await m_InitializationService.StartAsync(CancellationToken.None);

        // Assert - Should remain unchanged
        var retrievedCharacters = await m_CharacterRepository.GetCharactersAsync(GameName.HonkaiImpact3);
        Assert.That(retrievedCharacters, Has.Count.EqualTo(2));
        Assert.That(retrievedCharacters, Contains.Item("Kiana"));
        Assert.That(retrievedCharacters, Contains.Item("Mei"));
    }

    private void CreateTestCharacterJsonFile(string fileName, CharacterJsonModel model)
    {
        var jsonContent = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(m_TestAssetsPath, fileName);
        File.WriteAllText(filePath, jsonContent);
    }
}
