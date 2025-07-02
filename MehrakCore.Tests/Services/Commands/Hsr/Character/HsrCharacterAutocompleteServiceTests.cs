#region

using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Commands.Hsr.Character;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

#endregion

namespace MehrakCore.Tests.Services.Commands.Hsr.Character;

[Parallelizable(ParallelScope.Fixtures)]
public class HsrCharacterAutocompleteServiceTests
{
    private Mock<ICharacterCacheService> m_CharacterCacheServiceMock = null!;
    private HsrCharacterAutocompleteService m_AutocompleteService = null!;

    [SetUp]
    public void Setup()
    {
        m_CharacterCacheServiceMock = new Mock<ICharacterCacheService>();

        m_AutocompleteService = new HsrCharacterAutocompleteService(m_CharacterCacheServiceMock.Object);
    }

    [Test]
    public void FindCharacter_WithMatchingQuery_ReturnsFilteredResults()
    {
        // Arrange
        var testCharacters = new List<string>
        {
            "Kafka", "Blade", "Silver Wolf", "Luocha", "Seele", "Clara", "Himeko"
        };

        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(testCharacters);

        // Act
        var result = m_AutocompleteService.FindCharacter("Ka");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("Kafka"));

        m_CharacterCacheServiceMock.Verify(
            cache => cache.GetCharacters(GameName.HonkaiStarRail),
            Times.Once);
    }

    [Test]
    public void FindCharacter_WithCaseInsensitiveQuery_ReturnsMatchingResults()
    {
        // Arrange
        var testCharacters = new List<string>
        {
            "Kafka", "Blade", "Silver Wolf", "Luocha", "Seele", "Clara", "Himeko"
        };

        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(testCharacters);

        // Act
        var result = m_AutocompleteService.FindCharacter("SILVER");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result, Contains.Item("Silver Wolf"));
    }

    [Test]
    public void FindCharacter_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        var testCharacters = new List<string>
        {
            "Kafka", "Blade", "Silver Wolf", "Luocha", "Seele", "Clara", "Himeko"
        };

        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(testCharacters);

        // Act
        var result = m_AutocompleteService.FindCharacter("NonExistentCharacter");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindCharacter_WithManyResults_RespectsLimit()
    {
        // Arrange
        var testCharacters = new List<string>();
        for (int i = 1; i <= 30; i++)
        {
            testCharacters.Add($"Character{i}");
        }

        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(testCharacters);

        // Act
        var result = m_AutocompleteService.FindCharacter("Character");

        // Assert
        Assert.That(result, Has.Count.EqualTo(25)); // Should be limited to 25
    }

    [Test]
    public void FindCharacter_WithEmptyCache_ReturnsEmptyList()
    {
        // Arrange
        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(new List<string>());

        // Act
        var result = m_AutocompleteService.FindCharacter("Any");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindCharacter_WithPartialMatch_ReturnsAllMatches()
    {
        // Arrange
        var testCharacters = new List<string>
        {
            "Kafka", "Blade", "Silver Wolf", "Luocha", "Seele", "Clara", "Himeko"
        };

        m_CharacterCacheServiceMock
            .Setup(cache => cache.GetCharacters(GameName.HonkaiStarRail))
            .Returns(testCharacters);

        // Act
        var result = m_AutocompleteService.FindCharacter("e");

        // Assert
        Assert.That(result, Has.Count.EqualTo(4)); // Blade, Silver Wolf, Seele, Himeko
        Assert.That(result, Contains.Item("Blade"));
        Assert.That(result, Contains.Item("Silver Wolf"));
        Assert.That(result, Contains.Item("Seele"));
        Assert.That(result, Contains.Item("Himeko"));
    }
}
