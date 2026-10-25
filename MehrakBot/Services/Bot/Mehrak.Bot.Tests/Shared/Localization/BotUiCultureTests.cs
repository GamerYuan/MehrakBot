using System.Globalization;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Tests.TestUtils;
using Moq;
using NetCord;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;

namespace Mehrak.Bot.Tests.Shared.Localization;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class BotUiCultureTests
{
    [TestCase("de", "de-DE")]
    [TestCase("fr", "fr-FR")]
    [TestCase("ja", "ja-JP")]
    [TestCase("ko", "ko-KR")]
    [TestCase("zh-CN", "zh-CN")]
    [TestCase("zh-TW", "zh-TW")]
    [TestCase("pt-BR", "pt-BR")]
    [TestCase("ja-JP", "ja-JP")]
    [TestCase("PT-br", "pt-BR")]
    public void Resolve_SupportedDiscordLocale_ReturnsMappedCulture(string locale, string expected)
    {
        Assert.That(BotUiCulture.Resolve(locale).Name, Is.EqualTo(expected));
    }

    [TestCase("en-US")]
    [TestCase("en-GB")]
    [TestCase("es-ES")]
    [TestCase("pt-PT")]
    [TestCase("pl")]
    [TestCase("th")]
    [TestCase("unknown")]
    [TestCase("")]
    [TestCase(null)]
    public void Resolve_UnsupportedLocale_FallsBackToEnglish(string? locale)
    {
        Assert.That(BotUiCulture.Resolve(locale).Name, Is.EqualTo("en-US"));
    }

    [Test]
    public void SupportedCultures_CoverEveryTranslatedResourceSet()
    {
        Assert.That(BotUiCulture.SupportedCultures.Select(x => BotUiCulture.ResourceName(x)),
            Is.EquivalentTo(new[] { "en", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "de-DE", "fr-FR", "pt-BR" }));
    }

    [TestCase("ja-JP", "ja")]
    [TestCase("ko-KR", "ko")]
    [TestCase("de-DE", "de")]
    [TestCase("fr-FR", "fr")]
    [TestCase("zh-CN", "zh-CN")]
    [TestCase("zh-TW", "zh-TW")]
    [TestCase("pt-BR", "pt-BR")]
    public void DiscordLocaleToken_ReturnsDiscordToken(string cultureName, string expected)
    {
        Assert.That(BotUiCulture.DiscordLocaleToken(CultureInfo.GetCultureInfo(cultureName)), Is.EqualTo(expected));
    }

    [Test]
    public void DiscordLocaleToken_EnglishHasNoToken()
    {
        Assert.That(BotUiCulture.DiscordLocaleToken(BotUiCulture.English), Is.Null);
    }

    [Test]
    public void Resolve_InteractionWithoutLocale_FallsBackToEnglish()
    {
        using var discord = new DiscordTestHelper();
        var context = new Mock<IInteractionContext>();
        context.SetupGet(x => x.Interaction).Returns(discord.CreateCommandInteraction(42));

        Assert.That(BotUiCulture.Resolve(context.Object).Name, Is.EqualTo("en-US"));
        Assert.That(BotUiCulture.Resolve((IInteractionContext?)null).Name, Is.EqualTo("en-US"));
    }

    [Test]
    public void Resolve_UsesInvokingUserLocaleNotGuildLocale()
    {
        var interaction = new ModalInteraction(new JsonInteraction
        {
            UserLocale = "ja",
            GuildLocale = "fr",
            Data = new JsonInteractionData { Components = [] },
            User = new JsonUser { Id = 42 },
            Channel = new JsonChannel { Id = 321, Type = ChannelType.TextGuildChannel },
            Entitlements = []
        }, null, null!, new RestClient());
        var context = new Mock<IInteractionContext>();
        context.SetupGet(x => x.Interaction).Returns(interaction);

        Assert.That(BotUiCulture.Resolve(context.Object).Name, Is.EqualTo("ja-JP"));
    }

    [Test]
    public void Resolve_DoesNotChangeAmbientCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.That(BotUiCulture.Resolve((string?)null).Name, Is.EqualTo("en-US"));
            Assert.That(BotUiCulture.Normalize(CultureInfo.GetCultureInfo("pt-PT")).Name, Is.EqualTo("en-US"));
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("de-DE"));
            Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("de-DE"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestCase("pt-PT", "en")]
    [TestCase("ja-JP", "ja-JP")]
    [TestCase("en-US", "en")]
    public void ResourceName_MapsToResourceFile(string cultureName, string expected)
    {
        Assert.That(BotUiCulture.ResourceName(CultureInfo.GetCultureInfo(cultureName)), Is.EqualTo(expected));
    }
}
