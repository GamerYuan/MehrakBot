using System.Globalization;
using Mehrak.Bot.Generated;
using Mehrak.Bot.Shared.Abstractions;
using Mehrak.Bot.Shared.Services;

namespace Mehrak.Bot.Tests.Shared.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class HelpTextRendererTests
{
    private static readonly ICommandUiLocalizer Localizer = CommandUiLocalizer.Default;

    private static string Render(string command, string subcommand = "", string culture = "en-US") =>
        HelpTextRenderer.Render(GeneratedHelpRegistry.Commands, command, subcommand, Localizer,
            CultureInfo.GetCultureInfo(culture));

    [Test]
    public void EnglishSubcommandHelp_KeepsLegacyMarkdownStructure()
    {
        var text = Render("genshin", "character");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith(
                "## /genshin character\n" +
                "Get character card\n" +
                "### Usage\n" +
                "```/genshin character <characters> [server] [profile]```\n" +
                "### Parameters\n" +
                "- `characters`: Character Names or Aliases (Case-insensitive, Comma-separated, Max 4)\n" +
                "- `server`: Server [Optional]\n" +
                "- `profile`: Profile Id (Defaults to 1) [Optional]\n" +
                "### Examples\n```"));
            Assert.That(text, Does.Contain("/genshin character characters:Nahida"));
            Assert.That(text, Does.EndWith("-# [List of Aliases](https://mehrakbot.com/docs?tab=alias)"));
        });
    }

    [Test]
    public void EnglishOverview_ReturnsRootDescriptionAndSubcommandLines()
    {
        var text = Render("genshin");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("## /genshin\nGenshin Toolbox\n### Subcommands\n"));
            Assert.That(text, Does.Contain("- `character`: Get character card"));
            Assert.That(text, Does.Contain("- `notes`: Get real-time notes"));
        });
    }

    [Test]
    public void JapaneseHelp_LocalisesHeadingsDescriptionsAndParameterLabelsButKeepsCanonicalNames()
    {
        var text = Render("genshin", "character", "ja-JP");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("## /genshin character\nキャラクターカードを取得\n### 使用方法\n"));
            Assert.That(text, Does.Contain("### パラメータ"));
            Assert.That(text, Does.Contain("- `server`：サーバー [任意]"));
            Assert.That(text, Does.Contain("### 使用例"));
            // Command names, parameter names and example values stay canonical.
            Assert.That(text, Does.Contain("```/genshin character <characters> [server] [profile]```"));
            Assert.That(text, Does.Contain("characters:Nahida"));
        });
    }

    [Test]
    public void JapaneseOverview_UsesLocalisedSubcommandHeading()
    {
        var text = Render("genshin", "", "ja-JP");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("## /genshin\n原神ツールボックス\n### サブコマンド\n"));
            Assert.That(text, Does.Contain("- `character`：キャラクターカードを取得"));
        });
    }

    [Test]
    public void AvailableCommands_IsLocalisedAndUnknownCommandsFallBackToIt()
    {
        var localised = Render("", "", "zh-CN");
        var english = Render("", "");

        Assert.Multiple(() =>
        {
            Assert.That(localised, Does.StartWith("可用指令：\n"));
            Assert.That(localised, Does.Contain("- `/genshin`\n  - `abyss`\n  - `character`"));
            Assert.That(localised, Does.EndWith("使用 `/help <command>` 或 `/help <command> <subcommand>` 查看详情。"));
            Assert.That(english, Does.StartWith("Available commands:"));
            Assert.That(Render("does-not-exist", "", "zh-CN"), Is.EqualTo(localised));
            Assert.That(Render("genshin", "does-not-exist", "zh-CN"), Is.EqualTo(Render("genshin", "", "zh-CN")));
        });
    }

    [Test]
    public void HelpIgnoredCommands_KeepMetadataButNeverAppearInHelp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GeneratedHelpRegistry.Commands.Where(x => x.IsHidden).Select(x => x.Path[0]),
                Is.EquivalentTo(new[] { "ping", "health" }));
            Assert.That(Render(""), Does.Not.Contain("`/ping`").And.Not.Contain("`/health`"));
            Assert.That(Render("ping"), Is.EqualTo(Render("")));
            Assert.That(Render("health"), Is.EqualTo(Render("")));
        });
    }

    [Test]
    public void HelplineLookupsAcceptSlashesAndMixedCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Render("/GENSHIN", "CHARACTER"), Is.EqualTo(Render("genshin", "character")));
            Assert.That(Render(" hyl "), Is.EqualTo(Render("hyl")));
        });
    }

    [TestCase("en-US", "-# v1.2.3  |  [Click](https://mehrakbot.com/docs) for documentation")]
    [TestCase("de-DE", "-# v1.2.3  |  [Klicke hier](https://mehrakbot.com/docs) für die Dokumentation")]
    public void Footer_IsLocalised(string culture, string expected)
    {
        Assert.That(HelpTextRenderer.RenderFooter("1.2.3", Localizer, CultureInfo.GetCultureInfo(culture)),
            Is.EqualTo(expected));
    }

    [Test]
    public void Registry_CoversEveryUserFacingCommandWithCanonicalPaths()
    {
        var paths = GeneratedHelpRegistry.Commands
            .Select(x => string.Join(" ", x.Path))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var expected in new[]
                     {
                         "genshin character", "genshin notes", "genshin codes", "genshin abyss", "genshin theater",
                         "genshin stygian", "genshin charlist", "hsr character", "hsr moc", "hsr pf", "hsr as",
                         "hsr aa", "hsr charlist", "zzz character", "zzz codes", "zzz shiyu", "zzz da", "zzz tower",
                         "hi3 battlesuit", "checkin", "hyl", "help", "profile add", "profile delete",
                         "profile list", "profile update"
                     })
                Assert.That(paths, Does.Contain(expected));

            Assert.That(GeneratedHelpRegistry.Commands.Where(x => x.Examples.Count == 0), Is.Empty,
                "Every command must keep generated usage examples");
            Assert.That(GeneratedHelpRegistry.Commands.Where(x => string.IsNullOrWhiteSpace(x.Usage)), Is.Empty);
        });
    }
}
