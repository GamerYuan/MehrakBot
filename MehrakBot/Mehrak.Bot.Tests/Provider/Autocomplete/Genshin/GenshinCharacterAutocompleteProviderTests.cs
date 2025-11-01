using Mehrak.Bot.Provider;
using Mehrak.Bot.Provider.Autocomplete.Genshin;
using Mehrak.Domain.Enums;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Tests.Provider.Autocomplete.Genshin;

/// <summary>
/// Unit tests for GenshinCharacterAutocompleteProvider validating autocomplete choices generation
/// and character search functionality.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GenshinCharacterAutocompleteProviderTests
{
    private Mock<ICharacterAutocompleteService> m_MockAutocompleteService = null!;
    private GenshinCharacterAutocompleteProvider m_Provider = null!;

    [SetUp]
    public void Setup()
    {
        m_MockAutocompleteService = new Mock<ICharacterAutocompleteService>();
        m_Provider = new GenshinCharacterAutocompleteProvider(m_MockAutocompleteService.Object);
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
        var (option, context) = CreateTestInputs("Hu");

        var expectedCharacters = new List<string> { "Hu Tao", "Huohuo" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Hu"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(2));
        Assert.That(choices[0].Name, Is.EqualTo("Hu Tao"));
        Assert.That(choices[0].StringValue, Is.EqualTo("Hu Tao"));
        Assert.That(choices[1].Name, Is.EqualTo("Huohuo"));
        Assert.That(choices[1].StringValue, Is.EqualTo("Huohuo"));
    }

    [Test]
    public async Task GetChoicesAsync_WithSingleMatch_ReturnsSingleChoice()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Diluc");

        var expectedCharacters = new List<string> { "Diluc" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Diluc"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Diluc"));
        Assert.That(choices[0].StringValue, Is.EqualTo("Diluc"));
    }

    [Test]
    public async Task GetChoicesAsync_WithNoMatches_ReturnsEmptyChoices()
    {
        // Arrange
        var (option, context) = CreateTestInputs("XYZ");

        var expectedCharacters = new List<string>();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "XYZ"))
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

        var expectedCharacters = new List<string> { "Diluc", "Jean", "Klee", "Venti" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, ""))
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
        var (option, context) = CreateTestInputs("Zho");

        var expectedCharacters = new List<string> { "Zhongli" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Zho"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Zhongli"));
    }

    [Test]
    public async Task GetChoicesAsync_WithLowercaseQuery_CallsServiceWithSameCase()
    {
        // Arrange
        var (option, context) = CreateTestInputs("raiden");

        var expectedCharacters = new List<string> { "Raiden Shogun" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "raiden"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.Genshin, "raiden"), Times.Once);
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetChoicesAsync_WithMultipleMatchingCharacters_ReturnsAllMatches()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Tar");

        var expectedCharacters = new List<string> { "Tartaglia", "Childe" }; // Tartaglia's alias

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Tar"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetChoicesAsync_CallsServiceWithCorrectQuery()
    {
        // Arrange
        const string query = "Ayaka";
        var (option, context) = CreateTestInputs(query);

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, query))
            .Returns(["Kamisato Ayaka"]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.Genshin, query), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_ChoiceNameAndValueAreIdentical()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Nahida");

        var expectedCharacters = new List<string> { "Nahida" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Nahida"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo(choice.StringValue));
        Assert.That(choice.Name, Is.EqualTo("Nahida"));
    }

    [Test]
    public async Task GetChoicesAsync_WithSpecialCharactersInName_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Hu");

        var expectedCharacters = new List<string> { "Hu Tao" }; // Name with space

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Hu"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.Multiple(() =>
        {
            Assert.That(choice.Name, Is.EqualTo("Hu Tao"));
            Assert.That(choice.StringValue, Is.EqualTo("Hu Tao"));
        });
    }

    [Test]
    public async Task GetChoicesAsync_ReturnsValueTask_CompletesSuccessfully()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Test");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Test"))
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
            "Albedo", "Alhaitham", "Aloy", "Amber", "Arataki Itto",
            "Arlecchino", "Ayaka", "Ayato", "Azhdaha"
        };

        m_MockAutocompleteService
                   .Setup(x => x.FindCharacter(Game.Genshin, "A"))
               .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(9));
        Assert.That(choices.Select(c => c.Name), Is.EquivalentTo(expectedCharacters));
    }

    #endregion

    #region Choice Properties Tests

    [Test]
    public async Task GetChoicesAsync_ChoicesHaveCorrectType()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Ganyu");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Ganyu"))
            .Returns(["Ganyu"]);

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

        var expectedCharacters = new List<string> { "Kaeya", "Kazuha", "Keqing", "Klee" };

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "K"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        var choices = result!.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(choices[0].Name, Is.EqualTo("Kaeya"));
            Assert.That(choices[1].Name, Is.EqualTo("Kazuha"));
            Assert.That(choices[2].Name, Is.EqualTo("Keqing"));
            Assert.That(choices[3].Name, Is.EqualTo("Klee"));
        });
    }

    #endregion

    #region Service Integration Tests

    [Test]
    public async Task GetChoicesAsync_ServiceReturnsReadOnlyList_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Xiao");

        IReadOnlyList<string> expectedCharacters = new List<string> { "Xiao" }.AsReadOnly();

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Xiao"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choices = result!.ToList();
        Assert.That(choices, Has.Count.EqualTo(1));
        Assert.That(choices[0].Name, Is.EqualTo("Xiao"));
    }

    [Test]
    public async Task GetChoicesAsync_ServiceCalledOnlyOnce()
    {
        // Arrange
        var (option, context) = CreateTestInputs("Yelan");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "Yelan"))
            .Returns(new List<string> { "Yelan" });

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.Genshin, It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task GetChoicesAsync_WithWhitespaceQuery_PassesToService()
    {
        // Arrange
        var (option, context) = CreateTestInputs("   ");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "   "))
            .Returns([]);

        // Act
        await m_Provider.GetChoicesAsync(option, context);

        // Assert
        m_MockAutocompleteService.Verify(x => x.FindCharacter(Game.Genshin, "   "), Times.Once);
    }

    [Test]
    public async Task GetChoicesAsync_WithNumericQuery_HandlesCorrectly()
    {
        // Arrange
        var (option, context) = CreateTestInputs("123");

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "123"))
            .Returns(new List<string>());

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
        var (option, context) = CreateTestInputs("神");

        var expectedCharacters = new List<string> { "神里绫华" }; // Chinese character name

        m_MockAutocompleteService
            .Setup(x => x.FindCharacter(Game.Genshin, "神"))
            .Returns(expectedCharacters);

        // Act
        var result = await m_Provider.GetChoicesAsync(option, context);

        // Assert
        Assert.That(result, Is.Not.Null);
        var choice = result!.First();
        Assert.That(choice.Name, Is.EqualTo("神里绫华"));
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
        AutocompleteInteraction interaction = new(new JsonInteraction()
        {
            Data = new()
            {
                Options = []
            },
            User = new()
            {
                Id = 1
            },
            Channel = new()
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
