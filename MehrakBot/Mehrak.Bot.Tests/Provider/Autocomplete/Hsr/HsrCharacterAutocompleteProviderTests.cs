#region

using Mehrak.Bot.Provider;
using Mehrak.Bot.Provider.Autocomplete.Hsr;
using Mehrak.Domain.Enums;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Tests.Provider.Autocomplete.Hsr;

/// <summary>
/// Unit tests for HsrCharacterAutocompleteProvider validating autocomplete choices generation
/// and character search functionality for Honkai: Star Rail.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HsrCharacterAutocompleteProviderTests
{
    private Mock<ICharacterAutocompleteService> m_MockAutocompleteService = null!;
    private HsrCharacterAutocompleteProvider m_Provider = null!;

    [SetUp]
    public void Setup()
    {
        m_MockAutocompleteService = new Mock<ICharacterAutocompleteService>();
        m_Provider = new HsrCharacterAutocompleteProvider(m_MockAutocompleteService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_MockAutocompleteService.Reset();
    }

    #region GetChoicesAsync Tests

    [Test]
    public async Task GetChoicesAsync_WithMatchingCharacters_ReturnsChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Dan");

        var expectedCharacters = new List<string> { "Dan Heng", "Dan Heng • Imbibitor Lunae" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Dan"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Dan Heng"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Dan Heng"));
            Assert.That(choices[1].Name, Is.EqualTo("Dan Heng • Imbibitor Lunae"));
            Assert.That(choices[1].StringValue, Is.EqualTo("Dan Heng • Imbibitor Lunae"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithSingleMatch_ReturnsSingleChoice()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Acheron");

        var expectedCharacters = new List<string> { "Acheron" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Acheron"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Acheron"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Acheron"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithNoMatches_ReturnsEmptyChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("XYZ");

        var expectedCharacters = new List<string>();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "XYZ"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Is.Empty);
    }

    [Test]
    public async Task GetChoicesAsync_WithEmptyQuery_ReturnsAllCharacters()
    {
        // Arrange
        var (option, context) = CreateTestInputs("");

        var expectedCharacters = new List<string> { "Acheron", "Argenti", "Asta", "Aventurine" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, ""))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task GetChoicesAsync_WithPartialName_ReturnsMatchingCharacters()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Jin");

        var expectedCharacters = new List<string> { "Jing Yuan" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Jin"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Jing Yuan"));
    }

    [Test]
    public async Task GetChoicesAsync_WithLowercaseQuery_CallsServiceWithSameCase()
    {
        // Arrange
        var (option, context) = CreateTestInputs("kafka");

        var expectedCharacters = new List<string> { "Kafka" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "kafka"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiStarRail, "kafka"), Times.Once);
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetChoicesAsync_WithMultipleMatchingCharacters_ReturnsAllMatches()
    {
        // Arrange
        var (option, context) = CreateTestInputs("B");

        var expectedCharacters = new List<string> { "Bailu", "Black Swan", "Blade", "Boothill", "Bronya" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "B"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task GetChoicesAsync_CallsServiceWithCorrectQuery()
    {
        // Arrange
        const string query = "Firefly";
        var (option, context) = CreateTestInputs(query);

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, query))
            .Returns(["Firefly"]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiStarRail, query), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_ChoiceNameAndValueAreIdentical()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Ruan Mei");

        var expectedCharacters = new List<string> { "Ruan Mei" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Ruan Mei"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo(choice.StringValue));
        Assert.That(choice.Name, Is.EqualTo("Ruan Mei"));
    }

    [Test]
    public async Task GetChoicesAsync_WithSpecialCharactersInName_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Dan Heng •");

        var expectedCharacters = new List<string> { "Dan Heng • Imbibitor Lunae" }; // Name with • character

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Dan Heng •"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo("Dan Heng • Imbibitor Lunae"));
            Assert.That(choice.StringValue, Is.EqualTo("Dan Heng • Imbibitor Lunae"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_ReturnsValueTask_CompletesSuccessfully()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Test");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Test"))
            .Returns([]);

        // Act
        var resultTask = m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(resultTask.IsCompleted, Is.True);
        var result = await resultTask;
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task GetChoicesAsync_WithLongCharacterList_ReturnsAllChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("S");

        var expectedCharacters = new List<string>
        {
            "Sampo", "Seele", "Serval", "Silver Wolf",
            "Sparkle", "Sushang", "Stelle"
        };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "S"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(7));
        Assert.That(choices.Select(c => c.Name), Is.EquivalentTo(expectedCharacters));
    }

    #endregion

    #region Choice Properties Tests

    [Test]
    public async Task GetChoicesAsync_ChoicesHaveCorrectType()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Topaz");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Topaz"))
            .Returns(["Topaz & Numby"]);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.InstanceOf<IEnumerable<ApplicationCommandOptionChoiceProperties>>());
        var choice = result!.First();
        Assert.That(choice, Is.InstanceOf<ApplicationCommandOptionChoiceProperties>());
    }

    [Test]
    public async Task GetChoicesAsync_PreservesCharacterOrder()
    {
        // Arrange
        var (option, context) = CreateTestInputs("L");

        var expectedCharacters = new List<string> { "Lingsha", "Luka", "Luocha", "Lynx" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "L"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choices = result!.ToList();
        Assert.That(choices[0].Name, Is.EqualTo("Lingsha"));
        Assert.That(choices[1].Name, Is.EqualTo("Luka"));
        Assert.That(choices[2].Name, Is.EqualTo("Luocha"));
        Assert.That(choices[3].Name, Is.EqualTo("Lynx"));
    }

    #endregion

    #region Service Integration Tests

    [Test]
    public async Task GetChoicesAsync_ServiceReturnsReadOnlyList_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Welt");

        IReadOnlyList<string> expectedCharacters = new List<string> { "Welt" }.AsReadOnly();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Welt"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Welt"));
    }

    [Test]
    public async Task GetChoicesAsync_ServiceCalledOnlyOnce()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Yunli");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Yunli"))
            .Returns(new List<string> { "Yunli" });

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiStarRail, It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task GetChoicesAsync_WithWhitespaceQuery_PassesToService()
    {
        // Arrange
        var (option, context) = CreateTestInputs("   ");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "   "))
            .Returns([]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiStarRail, "   "), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_WithNumericQuery_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("123");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "123"))
            .Returns([]);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ToList(), Is.Empty);
    }

    [Test]
    public async Task GetChoicesAsync_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("符");

        var expectedCharacters = new List<string> { "符玄" }; // Chinese character name (Fu Xuan)

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "符"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo("符玄"));
    }

    [Test]
    public async Task GetChoicesAsync_WithTrailblazerVariants_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Trailblazer");

        var expectedCharacters = new List<string>
        {
            "Trailblazer (Destruction)",
            "Trailblazer (Preservation)",
            "Trailblazer (Harmony)",
            "Trailblazer (Remembrance)"
        };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Trailblazer"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(4));
        Assert.That(choices.Select(c => c.Name), Is.EquivalentTo(expectedCharacters));
    }

    [Test]
    public async Task GetChoicesAsync_WithAmpersandInName_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Topaz");

        var expectedCharacters = new List<string> { "Topaz & Numby" }; // Name with &

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiStarRail, "Topaz"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo("Topaz & Numby"));
            Assert.That(choice.StringValue, Is.EqualTo("Topaz & Numby"));
        });
    }

    #endregion

    #region Helpers

    private static (ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
        CreateTestInputs(string optionValue)
    {
        DiscordTestHelper testHelper = new();
        JsonApplicationCommandInteractionDataOption jsonOption = new()
        {
            Value = optionValue
        };
        ApplicationCommandInteractionDataOption option = new(jsonOption);
        AutocompleteInteraction interaction = new(new JsonInteraction
        {
            Data = new JsonInteractionData
            {
                Options = []
            },
            User = new JsonUser
            {
                Id = 1
            },
            Channel = new JsonChannel
            {
                Id = 1
            },
            Entitlements = []
        }, null, (_, _, _, _, _) => Task.FromResult<InteractionCallbackResponse?>(null), testHelper.DiscordClient.Rest);
        AutocompleteInteractionContext context = new(interaction, testHelper.DiscordClient);
        return (option, context);
    }

    #endregion
}
