#region

using Mehrak.Bot.Provider;
using Mehrak.Bot.Provider.Autocomplete.Hi3;
using Mehrak.Domain.Enums;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

#endregion

namespace Mehrak.Bot.Tests.Provider.Autocomplete.Hi3;

/// <summary>
/// Unit tests for Hi3CharacterAutocompleteProvider validating autocomplete choices generation
/// and character search functionality for Honkai Impact 3rd.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class Hi3CharacterAutocompleteProviderTests
{
    private Mock<ICharacterAutocompleteService> m_MockAutocompleteService = null!;
    private Hi3CharacterAutocompleteProvider m_Provider = null!;

    [SetUp]
    public void Setup()
    {
        m_MockAutocompleteService = new Mock<ICharacterAutocompleteService>();
        m_Provider = new Hi3CharacterAutocompleteProvider(m_MockAutocompleteService.Object);
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
        var (option, context) = CreateTestInputs("Kia");

        var expectedCharacters = new List<string> { "Kiana Kaslana", "Kiana" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Kia"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Kiana Kaslana"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Kiana Kaslana"));
            Assert.That(choices[1].Name, Is.EqualTo("Kiana"));
            Assert.That(choices[1].StringValue, Is.EqualTo("Kiana"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithSingleMatch_ReturnsSingleChoice()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Elysia");

        var expectedCharacters = new List<string> { "Elysia" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Elysia"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Elysia"));
            Assert.That(choices[0].StringValue, Is.EqualTo("Elysia"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_WithNoMatches_ReturnsEmptyChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("XYZ");

        var expectedCharacters = new List<string>();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "XYZ"))
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

        var expectedCharacters = new List<string> { "Kiana", "Mei", "Bronya", "Himeko" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, ""))
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
        var (option, context) = CreateTestInputs("Bro");

        var expectedCharacters = new List<string> { "Bronya Zaychik" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Bro"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Bronya Zaychik"));
    }

    [Test]
    public async Task GetChoicesAsync_WithLowercaseQuery_CallsServiceWithSameCase()
    {
        // Arrange
        var (option, context) = CreateTestInputs("mei");

        var expectedCharacters = new List<string> { "Raiden Mei" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "mei"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiImpact3, "mei"), Times.Once);
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetChoicesAsync_WithMultipleMatchingCharacters_ReturnsAllMatches()
    {
        // Arrange
        var (option, context) = CreateTestInputs("S");

        var expectedCharacters = new List<string> { "Seele", "Senti", "Susannah" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "S"))
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
        const string query = "Fu Hua";
        var (option, context) = CreateTestInputs(query);

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, query))
            .Returns(["Fu Hua"]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiImpact3, query), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_ChoiceNameAndValueAreIdentical()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Durandal");

        var expectedCharacters = new List<string> { "Durandal" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Durandal"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo(choice.StringValue));
        Assert.That(choice.Name, Is.EqualTo("Durandal"));
    }

    [Test]
    public async Task GetChoicesAsync_WithSpecialCharactersInName_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Herrscher");

        var expectedCharacters = new List<string> { "Herrscher of Human: Ego" }; // Name with colon

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Herrscher"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo("Herrscher of Human: Ego"));
            Assert.That(choice.StringValue, Is.EqualTo("Herrscher of Human: Ego"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_ReturnsValueTask_CompletesSuccessfully()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Test");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Test"))
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
        var (option, context) = CreateTestInputs("R");

        var expectedCharacters = new List<string>
        {
            "Raiden Mei", "Rita Rossweisse", "Rozaliya Olenyeva", "Raven"
        };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "R"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(4));
        Assert.That(choices.Select(c => c.Name), Is.EquivalentTo(expectedCharacters));
    }

    #endregion

    #region Choice Properties Tests

    [Test]
    public async Task GetChoicesAsync_ChoicesHaveCorrectType()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Theresa");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Theresa"))
            .Returns(["Theresa Apocalypse"]);

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
        var (option, context) = CreateTestInputs("M");

        var expectedCharacters = new List<string> { "Mei", "Mobius", "Murata Himeko" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "M"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choices = result!.ToList();
        Assert.That(choices[0].Name, Is.EqualTo("Mei"));
        Assert.That(choices[1].Name, Is.EqualTo("Mobius"));
        Assert.That(choices[2].Name, Is.EqualTo("Murata Himeko"));
    }

    #endregion

    #region Service Integration Tests

    [Test]
    public async Task GetChoicesAsync_ServiceReturnsReadOnlyList_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Aponia");

        IReadOnlyList<string> expectedCharacters = new List<string> { "Aponia" }.AsReadOnly();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Aponia"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Aponia"));
    }

    [Test]
    public async Task GetChoicesAsync_ServiceCalledOnlyOnce()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Vill-V");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "Vill-V"))
            .Returns(new List<string> { "Vill-V" });

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiImpact3, It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task GetChoicesAsync_WithWhitespaceQuery_PassesToService()
    {
        // Arrange
        var (option, context) = CreateTestInputs("   ");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "   "))
            .Returns([]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.HonkaiImpact3, "   "), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_WithNumericQuery_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("13");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "13"))
            .Returns(["The 13 Flame-Chasers"]); // Hypothetical match

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("The 13 Flame-Chasers"));
    }

    [Test]
    public async Task GetChoicesAsync_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("琪");

        var expectedCharacters = new List<string> { "琪亚娜" }; // Kiana

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.HonkaiImpact3, "琪"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo("琪亚娜"));
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
