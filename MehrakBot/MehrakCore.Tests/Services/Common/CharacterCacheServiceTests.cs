#region

using Mehrak.Domain.Repositories;
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
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        // Act
        var result = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);

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
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        // Act
        await m_CharacterCacheService.UpdateCharactersAsync(Game.HonkaiStarRail);
        var result = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);

        // Assert
        Assert.That(result, Is.EqualTo(testCharacters));
        m_CharacterRepositoryMock.Verify(repo => repo.GetCharactersAsync(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public async Task UpdateAllCharactersAsync_WhenCalled_UpdatesAllGameCaches()
    {
        // Arrange
        var hsrCharacters = new List<string> { "HSR_Character1", "HSR_Character2" };
        var genshinCharacters = new List<string> { "Genshin_Character1", "Genshin_Character2" };

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.Genshin))
            .ReturnsAsync(genshinCharacters);

        // Act
        await m_CharacterCacheService.UpdateAllCharactersAsync();

        // Assert
        var hsrResult = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);
        var genshinResult = m_CharacterCacheService.GetCharacters(Game.Genshin);

        Assert.That(hsrResult, Is.EqualTo(hsrCharacters));
        Assert.That(genshinResult, Is.EqualTo(genshinCharacters));

        // Verify all games were updated
        foreach (var game in Enum.GetValues<Game>())
            m_CharacterRepositoryMock.Verify(repo => repo.GetCharactersAsync(game), Times.Once);
    }

    [Test]
    public async Task GetCacheStatus_WhenCachePopulated_ReturnsCorrectCounts()
    {
        // Arrange
        var hsrCharacters = new List<string> { "HSR1", "HSR2", "HSR3" };
        var genshinCharacters = new List<string> { "Genshin1", "Genshin2" };

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.Genshin))
            .ReturnsAsync(genshinCharacters);

        // Act
        await m_CharacterCacheService.UpdateCharactersAsync(Game.HonkaiStarRail);
        await m_CharacterCacheService.UpdateCharactersAsync(Game.Genshin);
        var status = m_CharacterCacheService.GetCacheStatus();

        // Assert
        Assert.That(status[Game.HonkaiStarRail], Is.EqualTo(3));
        Assert.That(status[Game.Genshin], Is.EqualTo(2));
    }

    [Test]
    public async Task ClearCache_WhenCalled_ClearsAllCaches()
    {
        // Arrange
        var testCharacters = new List<string> { "Character1", "Character2" };
        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(testCharacters);

        await m_CharacterCacheService.UpdateCharactersAsync(Game.HonkaiStarRail);

        // Act
        m_CharacterCacheService.ClearCache();
        var result = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);

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
            .Setup(repo => repo.GetCharactersAsync(Game.HonkaiStarRail))
            .ReturnsAsync(hsrCharacters);

        m_CharacterRepositoryMock
            .Setup(repo => repo.GetCharactersAsync(Game.Genshin))
            .ReturnsAsync(genshinCharacters);

        await m_CharacterCacheService.UpdateCharactersAsync(Game.HonkaiStarRail);
        await m_CharacterCacheService.UpdateCharactersAsync(Game.Genshin);

        // Act
        m_CharacterCacheService.ClearCache(Game.HonkaiStarRail);

        // Assert
        var hsrResult = m_CharacterCacheService.GetCharacters(Game.HonkaiStarRail);
        var genshinResult = m_CharacterCacheService.GetCharacters(Game.Genshin);

        Assert.That(hsrResult, Is.Empty);
        Assert.That(genshinResult, Is.EqualTo(genshinCharacters));
    }
}