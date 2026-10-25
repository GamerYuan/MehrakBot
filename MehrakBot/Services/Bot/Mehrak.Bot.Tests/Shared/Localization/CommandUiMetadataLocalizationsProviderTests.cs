using Mehrak.Bot.Generated;
using Mehrak.Bot.Shared.Models;
using Mehrak.Bot.Shared.Services;
using Mehrak.Domain.Shared.Enums;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Tests.Shared.Localization;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class CommandUiMetadataLocalizationsProviderTests
{
    private static readonly string[] DiscordLocales =
        ["de", "fr", "ja", "ko", "pt-BR", "zh-CN", "zh-TW"];

    private CommandUiMetadataLocalizationsProvider m_Provider = null!;

    [SetUp]
    public void SetUp()
    {
        m_Provider = new CommandUiMetadataLocalizationsProvider(CommandUiLocalizer.Default,
            GeneratedHelpRegistry.Commands);
    }

    private static DescriptionLocalizationPathSegment Description() => DescriptionLocalizationPathSegment.Instance;

    [Test]
    public async Task NativeNetCordMetadata_LocalisesAllDescriptionsAndChoicesWithoutChangingContracts()
    {
        var localized = new ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext>(
            ApplicationCommandServiceConfiguration<ApplicationCommandContext>.Default with
            {
                LocalizationsProvider = m_Provider
            });
        var neutral = new ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext>();
        localized.AddModules(typeof(Program).Assembly);
        neutral.AddModules(typeof(Program).Assembly);

        var neutralCommands = neutral.GetCommands().ToDictionary(x => x.Name);
        foreach (var command in localized.GetCommands())
        {
            var actual = (SlashCommandProperties)await command.GetRawValueAsync();
            var original = (SlashCommandProperties)await neutralCommands[command.Name].GetRawValueAsync();
            Assert.Multiple(() =>
            {
                Assert.That(actual.Name, Is.EqualTo(original.Name));
                Assert.That(actual.NameLocalizations, Is.Null.Or.Empty);
                Assert.That(actual.Description, Is.EqualTo(original.Description));
                Assert.That(actual.DescriptionLocalizations?.Keys ?? [], Is.EquivalentTo(DiscordLocales), actual.Name);
                Assert.That(actual.DefaultGuildPermissions, Is.EqualTo(original.DefaultGuildPermissions));
                Assert.That(actual.Contexts, Is.EqualTo(original.Contexts));
            });
            AssertOptions(actual.Options ?? [], original.Options ?? [], actual.Name);
        }
    }

    private static void AssertOptions(IEnumerable<ApplicationCommandOptionProperties> actualOptions,
        IEnumerable<ApplicationCommandOptionProperties> originalOptions, string path)
    {
        var originals = originalOptions.ToDictionary(x => x.Name);
        foreach (var actual in actualOptions)
        {
            var original = originals[actual.Name];
            var optionPath = path + " " + actual.Name;
            Assert.Multiple(() =>
            {
                Assert.That(actual.NameLocalizations, Is.Null.Or.Empty, optionPath);
                Assert.That(actual.Description, Is.EqualTo(original.Description));
                Assert.That(actual.DescriptionLocalizations?.Keys ?? [], Is.EquivalentTo(DiscordLocales), optionPath);
                Assert.That(actual.Type, Is.EqualTo(original.Type));
                Assert.That(actual.Required, Is.EqualTo(original.Required));
                Assert.That(actual.Autocomplete, Is.EqualTo(original.Autocomplete));
            });
            var originalChoices = (original.Choices ?? []).ToDictionary(x => x.Name);
            foreach (var choice in actual.Choices ?? [])
            {
                Assert.Multiple(() =>
                {
                    Assert.That(choice.NameLocalizations?.Keys ?? [], Is.EquivalentTo(DiscordLocales), optionPath + " " + choice.Name);
                    Assert.That(choice.StringValue, Is.EqualTo(originalChoices[choice.Name].StringValue));
                    Assert.That(choice.NumericValue, Is.EqualTo(originalChoices[choice.Name].NumericValue));
                    Assert.That(choice.ValueType, Is.EqualTo(originalChoices[choice.Name].ValueType));
                });
            }
            AssertOptions(actual.Options ?? [], original.Options ?? [], optionPath);
        }
    }

    [TestCase("ping", "接続を確認！")]
    [TestCase("health", "ボットと各サービスの状態を確認します。")]
    public void HelpIgnoredCommands_StillHaveLocalisedMetadata(string command, string expected)
    {
        var result = m_Provider.Resolve([new ApplicationCommandLocalizationPathSegment(command), Description()]);
        Assert.That(result?["ja"], Is.EqualTo(expected));
    }

    [Test]
    public void NestedGroupPath_ResolvesTheExactDescriptorAndRejectsUnknownSubcommands()
    {
        var provider = new CommandUiMetadataLocalizationsProvider(CommandUiLocalizer.Default,
        [
            new HelpCommandDescriptor(["tool", "group", "show"], "Get character card", "Genshin Toolbox", true,
                "/tool group show", [], [new HelpParameterDescriptor("server", "Server", false, [])], null)
        ]);
        var path = new List<LocalizationPathSegment>
        {
            new ApplicationCommandLocalizationPathSegment("tool"),
            new SubSlashCommandGroupLocalizationPathSegment("group"),
            new SubSlashCommandLocalizationPathSegment("show")
        };
        Assert.That(provider.Resolve([.. path, Description()])?["ja"], Is.EqualTo("キャラクターカードを取得"));
        Assert.That(provider.Resolve([.. path, new SlashCommandParameterLocalizationPathSegment("server"), Description()])?["ja"],
            Is.EqualTo("サーバー"));
        Assert.That(provider.Resolve([path[0], path[1], new SubSlashCommandLocalizationPathSegment("missing"), Description()]),
            Is.Null);
    }

    [Test]
    public void RootCommandDescription_ResolvesEverySupportedLocaleWithCanonicalKeys()
    {
        var result = m_Provider.Resolve([new ApplicationCommandLocalizationPathSegment("genshin"), Description()]);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Keys, Is.EquivalentTo(DiscordLocales));
            Assert.That(result["ja"], Is.EqualTo("原神ツールボックス"));
            Assert.That(result["zh-CN"], Is.EqualTo("原神工具箱"));
            Assert.That(result["de"], Is.EqualTo("Genshin-Werkzeuge"));
            Assert.That(result["pt-BR"], Is.EqualTo("Ferramentas de Genshin"));
        });
    }

    [Test]
    public void SubcommandDescription_IsResolvedFromNestedPath()
    {
        var result = m_Provider.Resolve(
        [
            new ApplicationCommandLocalizationPathSegment("genshin"),
            new SubSlashCommandLocalizationPathSegment("character"),
            Description()
        ]);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["zh-TW"], Is.EqualTo("取得角色卡片"));
    }

    [Test]
    public void ParameterDescription_IsResolvedFromParameterPath()
    {
        var result = m_Provider.Resolve(
        [
            new ApplicationCommandLocalizationPathSegment("genshin"),
            new SubSlashCommandLocalizationPathSegment("character"),
            new SlashCommandParameterLocalizationPathSegment("server"),
            Description()
        ]);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["ko"], Is.EqualTo("서버"));
    }

    [Test]
    public void UnknownParameterOrCommand_ReturnsNullSoDiscordKeepsTheEnglishDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_Provider.Resolve(
            [
                new ApplicationCommandLocalizationPathSegment("genshin"),
                new SubSlashCommandLocalizationPathSegment("character"),
                new SlashCommandParameterLocalizationPathSegment("nope"),
                Description()
            ]), Is.Null);
            Assert.That(m_Provider.Resolve(
                [new ApplicationCommandLocalizationPathSegment("nope"), Description()]), Is.Null);
            Assert.That(m_Provider.Resolve([]), Is.Null);
        });
    }

    [Test]
    public void NamesAreNotLocalisedInThisRelease()
    {
        Assert.That(m_Provider.Resolve(
        [
            new ApplicationCommandLocalizationPathSegment("genshin"),
            new SubSlashCommandLocalizationPathSegment("character"),
            new SlashCommandParameterLocalizationPathSegment("characters"),
            NameLocalizationPathSegment.Instance
        ]), Is.Null);
    }

    [Test]
    public void EnumChoiceLabels_AreResolvedFromEnumTypeAndField()
    {
        var serverField = typeof(Server).GetField(nameof(Server.Asia))!;
        var result = m_Provider.Resolve(
        [
            new EnumLocalizationPathSegment(typeof(Server)),
            new EnumFieldLocalizationPathSegment(serverField),
            NameLocalizationPathSegment.Instance
        ]);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!["zh-CN"], Is.EqualTo("亚服"));
            Assert.That(result["ja"], Is.EqualTo("アジア"));
            Assert.That(result["fr"], Is.EqualTo("Asie"));
        });
    }

    [Test]
    public void EnumChoiceLabels_UseExplicitChoiceNamesForLanguageOptions()
    {
        var japaneseField = typeof(WikiLocaleChoice).GetField(nameof(WikiLocaleChoice.JP))!;
        var result = m_Provider.Resolve(
        [
            new EnumLocalizationPathSegment(typeof(WikiLocaleChoice)),
            new EnumFieldLocalizationPathSegment(japaneseField),
            NameLocalizationPathSegment.Instance
        ]);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!["ja"], Is.EqualTo("日本語"));
        Assert.That(result["de"], Is.EqualTo("Japanisch"));
    }

    [Test]
    public void AllMetadataWithinDiscordLengthLimitsForEveryLocale()
    {
        foreach (var command in GeneratedHelpRegistry.Commands)
        {
            var commandPath = new List<LocalizationPathSegment>
            {
                new ApplicationCommandLocalizationPathSegment(command.Path[0])
            };
            if (command.Path.Count > 1)
                commandPath.Add(new SubSlashCommandLocalizationPathSegment(command.Path[1]));

            AssertTranslationsWithinLimit(commandPath.Append(Description()).ToList(),
                CommandUiMetadataLocalizationsProvider.MaxDescriptionLength);

            foreach (var parameter in command.Parameters)
            {
                var parameterPath = new List<LocalizationPathSegment>(commandPath)
                {
                    new SlashCommandParameterLocalizationPathSegment(parameter.Name),
                    Description()
                };
                AssertTranslationsWithinLimit(parameterPath,
                    CommandUiMetadataLocalizationsProvider.MaxDescriptionLength);
            }
        }

        foreach (var enumType in new[] { typeof(Server), typeof(Hi3Server), typeof(WikiLocaleChoice) })
        {
            foreach (var field in enumType.GetFields()
                         .Where(x => x is { IsStatic: true, IsLiteral: true }))
            {
                AssertTranslationsWithinLimit(
                [
                    new EnumLocalizationPathSegment(enumType),
                    new EnumFieldLocalizationPathSegment(field),
                    NameLocalizationPathSegment.Instance
                ], CommandUiMetadataLocalizationsProvider.MaxChoiceNameLength);
            }
        }
    }

    private void AssertTranslationsWithinLimit(IReadOnlyList<LocalizationPathSegment> path, int maxLength)
    {
        var result = m_Provider.Resolve(path);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Keys, Is.EquivalentTo(DiscordLocales));

        foreach (var (locale, value) in result)
        {
            Assert.That(DiscordLocales, Does.Contain(locale));
            Assert.That(value, Is.Not.Empty);
            Assert.That(value.Length, Is.LessThanOrEqualTo(maxLength),
                $"{locale} value for '{string.Join(" / ", path.Select(x => x.GetType().Name))}' exceeds {maxLength} characters");
        }
    }
}
