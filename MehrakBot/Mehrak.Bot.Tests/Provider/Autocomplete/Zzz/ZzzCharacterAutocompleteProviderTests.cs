#region

using Mehrak.Bot.Provider;
using Mehrak.Bot.Provider.Autocomplete.Zzz;
using Mehrak.Domain.Enums;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Tests.Provider.Autocomplete.Zzz;

/// <summary>
/// Unit tests for ZzzCharacterAutocompleteProvider validating autocomplete choices generation
/// and character search functionality for Zenless Zone Zero.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ZzzCharacterAutocompleteProviderTests
{
    private Mock<ICharacterAutocompleteService> m_MockAutocompleteService = null!;
    private ZzzCharacterAutocompleteProvider m_Provider = null!;
    private DiscordTestHelper? m_TestHelper;

    [SetUp]
    public void Setup()
    {
        m_MockAutocompleteService = new Mock<ICharacterAutocompleteService>();
        m_Provider = new ZzzCharacterAutocompleteProvider(m_MockAutocompleteService.Object);
    }

    [TearDown]
    public void TearDown()
    {
        m_TestHelper?.Dispose();
    }

    #region GetChoicesAsync Tests

    [Test]
    public async Task GetChoicesAsync_WithMatchingCharacters_ReturnsChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Ell");

        var expectedCharacters = new List<string> { "Ellen", "Ellen Joe" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Ell"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Ellen"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Ellen"));
            Assert.That(choices[1].Name, Is.EqualTo("Ellen Joe"));
            Assert.That(choices[1].StringValue, Is.EqualTo("Ellen Joe"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithSingleMatch_ReturnsSingleChoice()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Zhu Yuan");

        var expectedCharacters = new List<string> { "Zhu Yuan" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Zhu Yuan"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Zhu Yuan"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Zhu Yuan"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithNoMatches_ReturnsEmptyChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("XYZ");

        var expectedCharacters = new List<string>();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "XYZ"))
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

        var expectedCharacters = new List<string> { "Anby", "Billy", "Nicole", "Nekomata" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, ""))
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
        var (option, context) = CreateTestInputs("Nic");

        var expectedCharacters = new List<string> { "Nicole Demara" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Nic"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Nicole Demara"));
    }

    [Test]
    public async Task GetChoicesAsync_WithLowercaseQuery_CallsServiceWithSameCase()
    {
        // Arrange
        var (option, context) = CreateTestInputs("lycaon");

        var expectedCharacters = new List<string> { "Lycaon" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "lycaon"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.ZenlessZoneZero, "lycaon"), Times.Once);
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetChoicesAsync_WithMultipleMatchingCharacters_ReturnsAllMatches()
    {
        // Arrange
        var (option, context) = CreateTestInputs("S");

        var expectedCharacters = new List<string> { "Seth", "Soldier 11", "Soukaku" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "S"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetChoicesAsync_CallsServiceWithCorrectQuery()
    {
        // Arrange
        const string query = "Jane";
        var (option, context) = CreateTestInputs(query);

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, query))
            .Returns(["Jane Doe"]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.ZenlessZoneZero, query), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_ChoiceNameAndValueAreIdentical()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Grace");

        var expectedCharacters = new List<string> { "Grace" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Grace"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo(choice.StringValue));
        Assert.That(choice.Name, Is.EqualTo("Grace"));
    }

    [Test]
    public async Task GetChoicesAsync_WithSpecialCharactersInName_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Soldier");

        var expectedCharacters = new List<string> { "Soldier 11" }; // Name with space/number

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Soldier"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo("Soldier 11"));
            Assert.That(choice.StringValue, Is.EqualTo("Soldier 11"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_ReturnsValueTask_CompletesSuccessfully()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Test");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Test"))
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
        var (option, context) = CreateTestInputs("A");

        var expectedCharacters = new List<string>
        {
            "Anby", "Anton", "Alexandrina"
        };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "A"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(3));
        Assert.That(choices.Select(c => c.Name), Is.EquivalentTo(expectedCharacters));
    }

    #endregion

    #region Choice Properties Tests

    [Test]
    public async Task GetChoicesAsync_ChoicesHaveCorrectType()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Ben");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Ben"))
            .Returns(["Ben Bigger"]);

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
        var (option, context) = CreateTestInputs("K");

        var expectedCharacters = new List<string> { "Koleda", "Karin" }; // Assuming Karin is a character or similar

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "K"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choices = result!.ToList();
        Assert.That(choices[0].Name, Is.EqualTo("Koleda"));
        Assert.That(choices[1].Name, Is.EqualTo("Karin"));
    }

    #endregion

    #region Service Integration Tests

    [Test]
    public async Task GetChoicesAsync_ServiceReturnsReadOnlyList_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Rina");

        IReadOnlyList<string> expectedCharacters = new List<string> { "Rina" }.AsReadOnly();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Rina"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Rina"));
    }

    [Test]
    public async Task GetChoicesAsync_ServiceCalledOnlyOnce()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Corin");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "Corin"))
            .Returns(["Corin"]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.ZenlessZoneZero, It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task GetChoicesAsync_WithWhitespaceQuery_PassesToService()
    {
        // Arrange
        var (option, context) = CreateTestInputs("   ");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "   "))
            .Returns([]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.ZenlessZoneZero, "   "), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_WithNumericQuery_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("11");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "11"))
            .Returns(["Soldier 11"]);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Soldier 11"));
    }

    [Test]
    public async Task GetChoicesAsync_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("雅");

        var expectedCharacters = new List<string> { "星见雅" }; // Miyabi

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.ZenlessZoneZero, "雅"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo("星见雅"));
    }

    #endregion

    #region Helpers

    private (ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
        CreateTestInputs(string optionValue)
    {
        m_TestHelper = new DiscordTestHelper();
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
        }, null, (_, _, _, _, _) => Task.FromResult<InteractionCallbackResponse?>(null), m_TestHelper.DiscordClient.Rest);
        AutocompleteInteractionContext context = new(interaction, m_TestHelper.DiscordClient);
        return (option, context);
    }

    #endregion
}
