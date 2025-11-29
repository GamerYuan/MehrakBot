#region

using Mehrak.Bot.Services;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Services.Abstractions;
using Moq;

#endregion

namespace Mehrak.Bot.Tests.Services;

/// <summary>
/// Unit tests for GenshinCharacterAutocompleteService validating character search,
/// filtering, case-insensitive matching, limit enforcement, and game parameter handling.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CharacterAutocompleteServiceTests
{
    private Mock<ICharacterCacheService> m_MockCacheService = null!;
    private CharacterAutocompleteService m_Service = null!;

    private const int ExpectedLimit = 25;

    [SetUp]
    public void Setup()
    {
        m_MockCacheService = new Mock<ICharacterCacheService>();
        m_Service = new CharacterAutocompleteService(m_MockCacheService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_MockCacheService.Reset();
    }

    #region FindCharacter Tests - Basic Functionality

    [Test]
    public void FindCharacter_WithMatchingQuery_ReturnsMatchingCharacters()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Jean", "Klee", "Venti", "Zhongli"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Di");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Diluc"));
    }

    [Test]
    public void FindCharacter_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Jean", "Klee", "Venti", "Zhongli"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "XYZ");

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindCharacter_WithEmptyQuery_ReturnsAllCharacters()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Jean", "Klee"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "");

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result, Is.EquivalentTo(allCharacters));
    }

    [Test]
    public void FindCharacter_WithPartialMatch_ReturnsMatchingCharacters()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Hu Tao", "Zhongli", "Xiao", "Ganyu", "Hutao"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Hu");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("Hu Tao"));
        Assert.That(result, Does.Contain("Hutao"));
    }

    #endregion

    #region FindCharacter Tests - Case Insensitivity

    [Test]
    public void FindCharacter_WithLowercaseQuery_MatchesCaseInsensitively()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Jean", "Klee", "Venti"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "diluc");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Diluc"));
    }

    [Test]
    public void FindCharacter_WithUppercaseQuery_MatchesCaseInsensitively()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Raiden Shogun", "Nahida", "Furina"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "RAIDEN");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Raiden Shogun"));
    }

    [Test]
    public void FindCharacter_WithMixedCaseQuery_MatchesCaseInsensitively()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Kamisato Ayaka", "Kamisato Ayato", "Kaedehara Kazuha"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "KaMiSaTo");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("Kamisato Ayaka"));
        Assert.That(result, Does.Contain("Kamisato Ayato"));
    }

    #endregion

    #region FindCharacter Tests - Limit Enforcement

    [Test]
    public void FindCharacter_WithMoreThan25Matches_ReturnsOnly25Characters()
    {
        // Arrange
        var allCharacters = Enumerable.Range(1, 50)
            .Select(i => $"Character{i}")
            .ToList();

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Character");

        // Assert
        Assert.That(result, Has.Count.EqualTo(ExpectedLimit));
    }

    [Test]
    public void FindCharacter_WithExactly25Matches_ReturnsAll25()
    {
        // Arrange
        var allCharacters = Enumerable.Range(1, 25)
            .Select(i => $"Character{i}")
            .ToList();

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Character");

        // Assert
        Assert.That(result, Has.Count.EqualTo(25));
    }

    [Test]
    public void FindCharacter_WithLessThan25Matches_ReturnsAllMatches()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Albedo", "Alhaitham", "Aloy", "Amber", "Arataki Itto"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "A");

        // Assert
        Assert.That(result, Has.Count.EqualTo(5));
    }

    [Test]
    public void FindCharacter_WithEmptyQueryAndMoreThan25Characters_Returns25()
    {
        // Arrange
        var allCharacters = Enumerable.Range(1, 30)
            .Select(i => $"Character{i}")
            .ToList();

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "");

        // Assert
        Assert.That(result, Has.Count.EqualTo(ExpectedLimit));
    }

    #endregion

    #region FindCharacter Tests - Character Name Patterns

    [Test]
    public void FindCharacter_WithSpacesInName_MatchesCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Hu Tao", "Raiden Shogun", "Yae Miko", "Sangonomiya Kokomi"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Hu Tao");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Hu Tao"));
    }

    [Test]
    public void FindCharacter_WithPartialSpaceQuery_MatchesCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Kaedehara Kazuha", "Kamisato Ayaka", "Kamisato Ayato", "Kuki Shinobu"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Kamisato A");

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("Kamisato Ayaka"));
        Assert.That(result, Does.Contain("Kamisato Ayato"));
    }

    [Test]
    public void FindCharacter_WithSingleCharacterQuery_ReturnsAllMatches()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Zhongli", "Xiao", "Xinyan", "Xingqiu", "Xianyun"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "X");

        // Assert
        Assert.That(result, Has.Count.EqualTo(4));
        Assert.That(result, Does.Not.Contain("Zhongli"));
    }

    #endregion

    #region FindCharacter Tests - Special Characters and Unicode

    [Test]
    public void FindCharacter_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "神里绫华", "雷电将军", "枫原万叶"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "神里");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("神里绫华"));
    }

    [Test]
    public void FindCharacter_WithSpecialCharactersInQuery_HandlesCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Tartaglia", "Childe", "Hu Tao"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Hu-Tao");

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region FindCharacter Tests - Edge Cases

    [Test]
    public void FindCharacter_WithWhitespaceQuery_MatchesCharactersWithSpaces()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Hu Tao", "Jean", "Klee"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, " ");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Hu Tao"));
    }

    [Test]
    public void FindCharacter_WithNumericQuery_HandlesCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Jean", "Character123"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "123");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Character123"));
    }

    [Test]
    public void FindCharacter_WithEmptyCacheList_ReturnsEmptyList()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns([]);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Diluc");

        // Assert
        Assert.That(result, Is.Empty);
    }

    #endregion

    #region FindCharacter Tests - Service Integration

    [Test]
    public void FindCharacter_CallsCacheServiceWithCorrectGame()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(["Diluc"]);

        // Act
        m_Service.FindCharacter(Game.Genshin, "Diluc");

        // Assert
        m_MockCacheService.Verify(x => x.GetCharacters(Game.Genshin), Times.Once);
    }

    [Test]
    public void FindCharacter_CalledMultipleTimes_CallsCacheServiceEachTime()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(["Diluc", "Jean"]);

        // Act
        m_Service.FindCharacter(Game.Genshin, "Di");
        m_Service.FindCharacter(Game.Genshin, "Je");
        m_Service.FindCharacter(Game.Genshin, "K");

        // Assert
        m_MockCacheService.Verify(x => x.GetCharacters(Game.Genshin), Times.Exactly(3));
    }

    [Test]
    public void FindCharacter_ReturnsReadOnlyList()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(["Diluc", "Jean"]);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "Di");

        // Assert
        Assert.That(result, Is.InstanceOf<IReadOnlyList<string>>());
    }

    #endregion

    #region FindCharacter Tests - Game Parameter Validation

    [Test]
    public void FindCharacter_WithGenshinGame_CallsCacheWithGenshin()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(["Diluc"]);

        // Act
        m_Service.FindCharacter(Game.Genshin, "Diluc");

        // Assert
        m_MockCacheService.Verify(x => x.GetCharacters(Game.Genshin), Times.Once);
        m_MockCacheService.Verify(x => x.GetCharacters(It.Is<Game>(g => g != Game.Genshin)), Times.Never);
    }

    [Test]
    public void FindCharacter_WithHonkaiStarRailGame_CallsCacheWithHSR()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.HonkaiStarRail))
            .Returns(["Acheron"]);

        // Act
        m_Service.FindCharacter(Game.HonkaiStarRail, "Acheron");

        // Assert
        m_MockCacheService.Verify(x => x.GetCharacters(Game.HonkaiStarRail), Times.Once);
        m_MockCacheService.Verify(x => x.GetCharacters(It.Is<Game>(g => g != Game.HonkaiStarRail)), Times.Never);
    }

    [Test]
    public void FindCharacter_WithZenlessZoneZeroGame_CallsCacheWithZZZ()
    {
        // Arrange
        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.ZenlessZoneZero))
            .Returns(["Ellen"]);

        // Act
        m_Service.FindCharacter(Game.ZenlessZoneZero, "Ellen");

        // Assert
        m_MockCacheService.Verify(x => x.GetCharacters(Game.ZenlessZoneZero), Times.Once);
        m_MockCacheService.Verify(x => x.GetCharacters(It.Is<Game>(g => g != Game.ZenlessZoneZero)), Times.Never);
    }

    [Test]
    public void FindCharacter_WithDifferentGames_UsesCorrectCache()
    {
        // Arrange
        var genshinCharacters = new List<string> { "Diluc", "Jean" };
        var hsrCharacters = new List<string> { "Acheron", "Kafka" };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(genshinCharacters);

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.HonkaiStarRail))
            .Returns(hsrCharacters);

        // Act
        IReadOnlyList<string> genshinResult = m_Service.FindCharacter(Game.Genshin, "Di");
        IReadOnlyList<string> hsrResult = m_Service.FindCharacter(Game.HonkaiStarRail, "Ach");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(genshinResult, Has.Count.EqualTo(1));
            Assert.That(genshinResult[0], Is.EqualTo("Diluc"));

            Assert.That(hsrResult, Has.Count.EqualTo(1));
            Assert.That(hsrResult[0], Is.EqualTo("Acheron"));
        });

        m_MockCacheService.Verify(x => x.GetCharacters(Game.Genshin), Times.Once);
        m_MockCacheService.Verify(x => x.GetCharacters(Game.HonkaiStarRail), Times.Once);
    }

    [Test]
    public void FindCharacter_SameQueryDifferentGames_ReturnsDifferentResults()
    {
        // Arrange
        var genshinCharacters = new List<string> { "Traveler (Anemo)", "Diluc", "Jean" };
        var hsrCharacters = new List<string> { "Trailblazer (Destruction)", "Acheron", "Kafka" };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(genshinCharacters);

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.HonkaiStarRail))
            .Returns(hsrCharacters);

        // Act
        IReadOnlyList<string> genshinResult = m_Service.FindCharacter(Game.Genshin, "Tra");
        IReadOnlyList<string> hsrResult = m_Service.FindCharacter(Game.HonkaiStarRail, "Tra");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(genshinResult, Has.Count.EqualTo(1));
            Assert.That(genshinResult[0], Is.EqualTo("Traveler (Anemo)"));

            Assert.That(hsrResult, Has.Count.EqualTo(1));
            Assert.That(hsrResult[0], Is.EqualTo("Trailblazer (Destruction)"));
        });
    }

    #endregion

    #region FindCharacter Tests - Real-World Scenarios

    [Test]
    public void FindCharacter_WithGenshinCharacterList_FiltersCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Albedo", "Alhaitham", "Aloy", "Amber", "Arataki Itto", "Arlecchino", "Baizhu", "Barbara", "Beidou",
            "Bennett", "Candace", "Charlotte", "Chevreuse", "Chiori", "Chongyun", "Clorinde", "Collei", "Cyno", "Dehya",
            "Diluc", "Diona", "Dori", "Emilie", "Eula", "Faruzan", "Fischl", "Freminet", "Furina", "Gaming", "Ganyu"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "C");

        // Assert
        Assert.That(result, Has.Count.EqualTo(11));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Arlecchino"));
            Assert.That(result, Does.Contain("Candace"));
            Assert.That(result, Does.Contain("Charlotte"));
            Assert.That(result, Does.Contain("Chevreuse"));
            Assert.That(result, Does.Contain("Chiori"));
            Assert.That(result, Does.Contain("Chongyun"));
            Assert.That(result, Does.Contain("Clorinde"));
            Assert.That(result, Does.Contain("Collei"));
            Assert.That(result, Does.Contain("Cyno"));
            Assert.That(result, Does.Contain("Diluc"));
            Assert.That(result, Does.Contain("Fischl"));
        });
    }

    [Test]
    public void FindCharacter_WithCommonPrefix_ReturnsAllMatching()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Kamisato Ayaka", "Kamisato Ayato", "Kaedehara Kazuha", "Kaeya", "Kaveh", "Keqing", "Kirara", "Klee",
            "Kujou Sara", "Kuki Shinobu"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "K");

        // Assert
        Assert.That(result, Has.Count.EqualTo(10));
    }

    [Test]
    public void FindCharacter_WithHSRCharacterList_FiltersCorrectly()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Acheron", "Argenti", "Arlan", "Asta", "Aventurine",
            "Bailu", "Black Swan", "Blade", "Boothill", "Bronya",
            "Clara", "Dan Heng", "Dr. Ratio", "Firefly", "Fu Xuan"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.HonkaiStarRail))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.HonkaiStarRail, "B");

        // Assert
        Assert.That(result, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Bailu"));
            Assert.That(result, Does.Contain("Black Swan"));
            Assert.That(result, Does.Contain("Blade"));
            Assert.That(result, Does.Contain("Boothill"));
            Assert.That(result, Does.Contain("Bronya"));
        });
    }

    #endregion

    #region FindCharacter Tests - Order Preservation

    [Test]
    public void FindCharacter_PreservesOriginalOrder()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Zhongli",
            "Xiao",
            "Ganyu",
            "Hu Tao",
            "Albedo"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result[0], Is.EqualTo("Zhongli"));
            Assert.That(result[1], Is.EqualTo("Xiao"));
            Assert.That(result[2], Is.EqualTo("Ganyu"));
            Assert.That(result[3], Is.EqualTo("Hu Tao"));
            Assert.That(result[4], Is.EqualTo("Albedo"));
        });
    }

    [Test]
    public void FindCharacter_WithFilteredResults_PreservesOriginalOrder()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Diona", "Dori", "Dehya", "Albedo", "Bennett"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "D");

        // Assert
        Assert.That(result, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            Assert.That(result[0], Is.EqualTo("Diluc"));
            Assert.That(result[1], Is.EqualTo("Diona"));
            Assert.That(result[2], Is.EqualTo("Dori"));
            Assert.That(result[3], Is.EqualTo("Dehya"));
            Assert.That(result[4], Is.EqualTo("Albedo"));
        });
    }

    #endregion

    #region FindCharacter Tests - Substring Matching

    [Test]
    public void FindCharacter_MatchesSubstringAnywhere()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Diluc", "Raiden Shogun", "Kamisato Ayaka"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "den");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Raiden Shogun"));
    }

    [Test]
    public void FindCharacter_WithMiddleNameMatch_ReturnsCharacter()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Kaedehara Kazuha", "Kamisato Ayaka", "Sangonomiya Kokomi"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "dehara");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Kaedehara Kazuha"));
    }

    [Test]
    public void FindCharacter_WithLastNameMatch_ReturnsCharacter()
    {
        // Arrange
        var allCharacters = new List<string>
        {
            "Kamisato Ayaka", "Kamisato Ayato", "Yoimiya"
        };

        m_MockCacheService
            .Setup(x => x.GetCharacters(Game.Genshin))
            .Returns(allCharacters);

        // Act
        IReadOnlyList<string> result = m_Service.FindCharacter(Game.Genshin, "yaka");

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Kamisato Ayaka"));
    }

    #endregion
}
