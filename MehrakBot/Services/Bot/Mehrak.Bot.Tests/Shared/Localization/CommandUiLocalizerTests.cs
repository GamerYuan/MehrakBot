using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Services;

namespace Mehrak.Bot.Tests.Shared.Localization;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public partial class CommandUiLocalizerTests
{
    /// <summary>Keys that intentionally keep their formatting or a technical proper noun.</summary>
    private static readonly HashSet<string> InvariantValues =
    [
        "Help.SubcommandLine", "Help.ParameterLine", "Help.AvailableRoot", "Help.AvailableSub",
        "Presenter.ItemLine", "Server", "Modal.PassphraseLabel", "Europe", "Health.Online", "Health.Offline"
    ];

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();

    private static IEnumerable<TestCaseData> SupportedCultureNames()
    {
        foreach (var culture in BotUiCulture.SupportedCultures)
            yield return new TestCaseData(culture.Name).SetArgDisplayNames(culture.Name);
    }

    [Test]
    public void Default_LoadsRealResourceFilesFromOutputDirectory()
    {
        var localizer = CommandUiLocalizer.Default;

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(localizer.ResourceDirectory), Is.True,
                $"Command UI resources must be copied next to the binary: {localizer.ResourceDirectory}");
            foreach (var culture in BotUiCulture.SupportedCultures)
                Assert.That(localizer.Load(culture), Is.Not.Empty, $"No resources loaded for {culture.Name}");
        });
    }

    [Test]
    public void NeutralResources_ContainEverySymbolicKey()
    {
        var neutral = CommandUiLocalizer.Default.Load(BotUiCulture.English);

        var constants = typeof(CommandUiKeys)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(x => x is { IsLiteral: true, IsInitOnly: false })
            .Select(x => (string?)x.GetRawConstantValue())
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        Assert.That(constants, Is.Not.Empty);
        Assert.That(constants.Where(x => !neutral.ContainsKey(x)), Is.Empty,
            "Every symbolic key used by the bot must exist in the neutral resource file");
    }

    [Test]
    public void NeutralResources_ContainEveryMetadataDescriptionUsedByTheHelpRegistry()
    {
        var neutral = CommandUiLocalizer.Default.Load(BotUiCulture.English);
        var registry = Mehrak.Bot.Generated.GeneratedHelpRegistry.Commands;

        var required = registry
            .SelectMany(x => new[] { x.Description, x.RootDescription, x.Notes }
                .Concat(x.Parameters.Select(p => p.Description)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.That(required, Is.Not.Empty);
        Assert.That(required.Where(x => !neutral.ContainsKey(x)), Is.Empty,
            "Discord metadata, help descriptions and help notes must share one resource source");
    }

    [TestCase("Server", "ja", "サーバー")]
    [TestCase("Server", "ko", "서버")]
    [TestCase("Server", "fr", "Serveur")]
    [TestCase("Genshin Toolbox", "de", "Genshin-Werkzeuge")]
    [TestCase("Server", "ja-JP", "サーバー")]
    [TestCase("Server", "zh-CN", "服务器")]
    [TestCase("Genshin Toolbox", "ko-KR", "원신 도구 모음")]
    [TestCase("Japanese", "de-DE", "Japanisch")]
    public void Translate_KnownMetadata_ReturnsIdiomaticTranslation(string english, string cultureName,
        string expected)
    {
        Assert.That(CommandUiLocalizer.Default.Translate(CultureInfo.GetCultureInfo(cultureName), english),
            Is.EqualTo(expected));
    }

    [Test]
    public void Translate_UnknownTextOrUnsupportedCulture_ReturnsEnglishSource()
    {
        var localizer = CommandUiLocalizer.Default;

        Assert.Multiple(() =>
        {
            Assert.That(localizer.Translate(CultureInfo.GetCultureInfo("ja-JP"), "Not a translated description"),
                Is.EqualTo("Not a translated description"));
            Assert.That(localizer.Translate(CultureInfo.GetCultureInfo("pt-PT"), "Server"), Is.EqualTo("Server"));
            Assert.That(localizer.Translate(null, "Server"), Is.EqualTo("Server"));
            Assert.That(localizer.Translate(CultureInfo.GetCultureInfo("ja-JP"), null), Is.Empty);
        });
    }

    [Test]
    public void Get_FormatsArgumentsAndFallsBackSafely()
    {
        var localizer = CommandUiLocalizer.Default;

        Assert.Multiple(() =>
        {
            Assert.That(localizer.Get(CultureInfo.GetCultureInfo("zh-CN"), CommandUiKeys.PresenterCharacterLimit, "4"),
                Is.EqualTo("超出单次请求的角色数量上限！（上限 4）"));
            Assert.That(localizer.Get(CultureInfo.GetCultureInfo("ja-JP"), CommandUiKeys.PresenterItemLine, "a", "b"),
                Is.EqualTo("a：b"));
            Assert.That(localizer.Get(CultureInfo.GetCultureInfo("pt-PT"), CommandUiKeys.HelpSubcommands),
                Is.EqualTo("### Subcommands"));
            Assert.That(localizer.Get(null, CommandUiKeys.HelpSubcommands), Is.EqualTo("### Subcommands"));
            Assert.That(localizer.Get(CultureInfo.GetCultureInfo("ja-JP"), "Missing.Symbolic.Key"),
                Is.EqualTo("An unexpected error occurred. Please try again later."));
        });
    }

    [Test]
    public void MissingOrMalformedLocaleResources_FallBackToNeutralEnglishNeverRawKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "en.json"), "{\"Help.Subcommands\":\"### Subcommands\"}");
            File.WriteAllText(Path.Combine(directory, "ja-JP.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "ko-KR.json"), "not JSON");
            var localizer = new CommandUiLocalizer(directory);

            foreach (var culture in BotUiCulture.SupportedCultures)
                Assert.That(localizer.Get(culture, CommandUiKeys.HelpSubcommands), Is.EqualTo("### Subcommands"));
            Assert.That(localizer.Get(BotUiCulture.English, "Unknown.Key", "never echo this argument"),
                Is.EqualTo("An unexpected error occurred. Please try again later."));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SupportedLocales_HaveCompleteKeyAndPlaceholderParity()
    {
        var localizer = CommandUiLocalizer.Default;
        var neutral = localizer.Load(BotUiCulture.English);

        foreach (var culture in BotUiCulture.SupportedCultures.Where(x => x.Name != BotUiCulture.English.Name))
        {
            var translated = localizer.Load(culture);

            Assert.Multiple(() =>
            {
                Assert.That(translated.Keys.Except(neutral.Keys), Is.Empty,
                    $"{culture.Name} contains keys that do not exist in the neutral file");
                Assert.That(neutral.Keys.Except(translated.Keys), Is.Empty,
                    $"{culture.Name} is missing translations");
            });

            foreach (var (key, englishValue) in neutral)
            {
                var expected = PlaceholderPattern().Matches(englishValue)
                    .Select(x => x.Groups[1].Value).OrderBy(x => x).ToArray();
                var actual = PlaceholderPattern().Matches(translated[key])
                    .Select(x => x.Groups[1].Value).OrderBy(x => x).ToArray();

                Assert.That(actual, Is.EqualTo(expected),
                    $"{culture.Name} placeholder mismatch for '{key}'");

                Assert.That(translated[key], Is.Not.Null.And.Not.Empty.And.Not.WhiteSpace);
                if (!InvariantValues.Contains(key))
                    Assert.That(translated[key], Is.Not.EqualTo(englishValue),
                        $"{culture.Name} still contains the English text for '{key}'");
            }
        }
    }

    [Test]
    public void AlternatingCultures_NeverLeakIntoEachOther()
    {
        var localizer = CommandUiLocalizer.Default;
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en-US"] = "### Subcommands",
            ["zh-CN"] = "### 子指令",
            ["zh-TW"] = "### 子指令",
            ["ja-JP"] = "### サブコマンド",
            ["ko-KR"] = "### 하위 명령어",
            ["de-DE"] = "### Unterbefehle",
            ["fr-FR"] = "### Sous-commandes",
            ["pt-BR"] = "### Subcomandos"
        };

        var names = expected.Keys.ToArray();

        Parallel.For(0, 400, i =>
        {
            var name = names[i % names.Length];
            var culture = CultureInfo.GetCultureInfo(name);
            Assert.That(localizer.Get(culture, CommandUiKeys.HelpSubcommands), Is.EqualTo(expected[name]));
            Assert.That(localizer.Translate(culture, "Server"), Is.EqualTo(
                localizer.Load(culture).TryGetValue("Server", out var server) ? server : "Server"));
        });
    }
}
