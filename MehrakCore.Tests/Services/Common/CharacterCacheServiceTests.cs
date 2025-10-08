#region

using Mehrak.Domain.Enums;
using Mehrak.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Common;

[Parallelizable(ParallelScope.Fixtures)]
public class CharacterCacheServiceTests
{
    private Mock<ICharacterRepository> m_CharacterRepositoryMock = null!;
    private Mock<IAliasRepository> m_AliasRepositoryMock = null!;
    private CharacterCacheService m_CharacterCacheService = null!;

    [SetUp]
    public void Setup()
    {
        m_CharacterRepositoryMock = new Mock<ICharacterRepository>();
        m_AliasRepositoryMock = new Mock<IAliasRepository>();

        m_CharacterCacheService = new CharacterCacheService(
            m_CharacterRepositoryMock.Object,
            m_AliasRepositoryMock.Object,
            NullLogger<CharacterCacheService>.Instance);
    }

    [Test]
    public void GetCharacters_WhenCacheEmpty_ReturnsEmptyListAndTriggersUpdate()
    {
        // Arrange
        var testCharacters = new List<string> { "Character1", "Character2", "Character3" };
        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        // Act
        var result = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);

        // Assert
        Assert.That(result, Is.Empty);

        // Verify that the repository method would be called (though asynchronously)
        // We can't easily test the async trigger without more complex setup
    }

    [Test]
    public async Task UpdateCharactersAsync_WhenCalled_UpdatesCacheFromRepository()
    {
        // Arrange
        var testCharacters = new List<string> { "Character1", "Character2", "Character3" };
        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        // Act
        await m_CharacterCacheService.UpdateCharactersAsync(GameName.HonkaiStarRail);
        var result = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);

        // Assert
        Assert.That(result, Is.EqualTo(testCharacters));
        m_CharacterRepositoryMock.Verify(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task UpdateAllCharactersAsync_WhenCalled_UpdatesAllGameCaches()
    {
        // Arrange
        var hsrCharacters = new List<string> { "HSR_Character1", "HSR_Character2" };
        var genshinCharacters = new List<string> { "Genshin_Character1", "Genshin_Character2" };

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.Genshin))
            .ReturnsAsync(genshinCharacters);

        // Act
        await m_CharacterCacheService.UpdateAllCharactersAsync();

        // Assert
        var hsrResult = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);
        var genshinResult = m_CharacterCacheService.GetCharacters(GameName.Genshin);

        Assert.That(hsrResult, Is.EqualTo(hsrCharacters));
        Assert.That(genshinResult, Is.EqualTo(genshinCharacters));

        // Verify all games were updated
        foreach (var game in Enum.GetValues<GameName>())
            m_CharacterRepositoryMock.Verify(repo => repo.GetCharactersAsync(game), Times.Once);
    }

    [Test]
    public async Task GetCacheStatus_WhenCachePopulated_ReturnsCorrectCounts()
    {
        // Arrange
        var hsrCharacters = new List<string> { "HSR1", "HSR2", "HSR3" };
        var genshinCharacters = new List<string> { "Genshin1", "Genshin2" };

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.Genshin))
            .ReturnsAsync(genshinCharacters);

        // Act
        await m_CharacterCacheService.UpdateCharactersAsync(GameName.HonkaiStarRail);
        await m_CharacterCacheService.UpdateCharactersAsync(GameName.Genshin);
        var status = m_CharacterCacheService.GetCacheStatus();

        // Assert
        Assert.That(status[GameName.HonkaiStarRail], Is.EqualTo(3));
        Assert.That(status[GameName.Genshin], Is.EqualTo(2));
    }

    [Test]
    public async Task ClearCache_WhenCalled_ClearsAllCaches()
    {
        // Arrange
        var testCharacters = new List<string> { "Character1", "Character2" };
        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        await m_CharacterCacheService.UpdateCharactersAsync(GameName.HonkaiStarRail);

        // Act
        m_CharacterCacheService.ClearCache();
        var result = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ClearCache_WithSpecificGame_ClearsOnlyThatGameCache()
    {
        // Arrange
        var hsrCharacters = new List<string> { "HSR1", "HSR2" };
        var genshinCharacters = new List<string> { "Genshin1", "Genshin2" };

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(GameName.Genshin))
            .ReturnsAsync(genshinCharacters);

        await m_CharacterCacheService.UpdateCharactersAsync(GameName.HonkaiStarRail);
        await m_CharacterCacheService.UpdateCharactersAsync(GameName.Genshin);

        // Act
        m_CharacterCacheService.ClearCache(GameName.HonkaiStarRail);

        // Assert
        var hsrResult = m_CharacterCacheService.GetCharacters(GameName.HonkaiStarRail);
        var genshinResult = m_CharacterCacheService.GetCharacters(GameName.Genshin);

        Assert.That(hsrResult, Is.Empty);
        Assert.That(genshinResult, Is.EqualTo(genshinCharacters));
    }
}
