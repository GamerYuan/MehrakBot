using System.Globalization;
using Mehrak.Bot.Shared.Extensions;
using Mehrak.Bot.Shared.Localization;
using Mehrak.Bot.Shared.Models;
using Mehrak.Bot.Shared.Services;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Bot.Tests.Shared.Services;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class CommandOutcomePresenterLocalizationTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.FromUnixTimeSeconds(1760000000);
    private static readonly DateTimeOffset RecoveryAt = ObservedAt.AddSeconds(123);
    private static readonly DateTimeOffset ResetAt = ObservedAt.AddDays(3);
    private static readonly DateTimeOffset NextResetAt = ResetAt.AddDays(7);

    private static readonly string[] SupportedLocaleNames =
        ["zh-CN", "zh-TW", "ja-JP", "ko-KR", "de-DE", "fr-FR", "pt-BR"];

    private static CommandPresentation Present(CommandOutcome outcome, string culture) =>
        CommandOutcomePresenter.Present(outcome, 42, CommandUiLocalizer.Default,
            CultureInfo.GetCultureInfo(culture));

    private static string[] Text(CommandPresentation presentation) => presentation.Components.SelectMany(x => x switch
    {
        CommandText text => new[] { text.ToFormattedString() },
        CommandSection section => section.Components.Select(t => t.ToFormattedString()),
        _ => []
    }).ToArray();

    [Test]
    public void CharacterLimit_IsLocalised()
    {
        var outcome = CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a"], 4);

        Assert.That(Text(Present(outcome, "zh-CN")),
            Is.EqualTo(new[] { "超出单次请求的角色数量上限！（上限 4）" }));
        Assert.That(Text(Present(outcome, "ja-JP")),
            Is.EqualTo(new[] { "1 回のリクエストで指定できるキャラクター数の上限を超えました！（上限 4）" }));
    }

    [Test]
    public void EndgameLockedAndEmpty_UseLocalisedModeNamesAndFloor()
    {
        var locked = EndgameOutcome.Locked(Game.Genshin, "uid", EndgameMode.ImaginariumTheater);
        var empty = EndgameOutcome.Empty(Game.Genshin, "uid", EndgameMode.SpiralAbyss, 12);

        Assert.Multiple(() =>
        {
            Assert.That(Text(Present(locked, "ja-JP")), Is.EqualTo(new[] { "幻想真境劇詩 は未解放です" }));
            Assert.That(Present(locked, "ja-JP").IsEphemeral, Is.False);
            Assert.That(Text(Present(empty, "zh-CN")), Is.EqualTo(new[] { "未找到 深境螺旋 的通关记录 （第 12 层）" }));
            Assert.That(Text(Present(empty, "pt-BR")), Is.EqualTo(new[] { "Nenhum registro de conclusão encontrado para Abismo Espiral (Andar 12)" }));
        });
    }

    [Test]
    public void EndgameCard_LocalisesTitleCycleAndFooterButKeepsAttachmentKeys()
    {
        var presentation = Present(EndgameOutcome.Card(Game.Genshin, "uid", EndgameMode.MemoryOfChaos,
            StorageReference.Attachment("hash.png"), ObservedAt, ResetAt), "ko-KR");
        var text = Text(presentation);

        Assert.Multiple(() =>
        {
            Assert.That(text[0], Is.EqualTo("### <@42>님의 혼돈의 기억 요약"));
            Assert.That(text[1], Does.Contain("주기 시작:").And.Contain("주기 종료:"));
            Assert.That(text[^1], Is.EqualTo("-# " + CommandUiLocalizer.Default.Get(
                CultureInfo.GetCultureInfo("ko-KR"), CommandUiKeys.PresenterApiLimitationFooter)));
            Assert.That(presentation.Attachments.Single().StorageFileName, Is.EqualTo("hash.png"));
        });
    }

    [Test]
    public void GenshinNotes_AreLocalisedWhileIconsAndTimestampsStayStructural()
    {
        var notes = new GenshinNotesOutcome("123", ObservedAt, new(120, 200, RecoveryAt),
            new(3, 5, ExpeditionOutcome.InProgress, RecoveryAt), new(200, 2400, null), 2, 3, ResetAt,
            new(true, NextResetAt));
        var text = Text(Present(notes, "zh-CN"));

        Assert.Multiple(() =>
        {
            Assert.That(text[0], Is.EqualTo("## UID：123 的实时便笺"));
            Assert.That(text, Does.Contain("### 原粹树脂"));
            Assert.That(text, Does.Contain("### 探索派遣"));
            Assert.That(text, Does.Contain("### 尘歌壶"));
            Assert.That(text, Does.Contain("### 周本"));
            Assert.That(text, Does.Contain("剩余树脂减免：2/3"));
            Assert.That(text, Does.Contain("### 参量质变仪"));
            Assert.That(text.Where(x => x.StartsWith("-# ", StringComparison.Ordinal)),
                Does.Contain("-# 等待收取")
                    .And.Contain($"-# <t:{ResetAt.ToUnixTimeSeconds()}:R> 重置"));
            Assert.That(text.Any(x => x.Contains("<t:1760000123:R>", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void HsrAndZzzNotes_AreLocalised()
    {
        var hsr = Text(Present(new HsrNotesOutcome("123", ObservedAt, new(300, 300, null), 0, 3, ResetAt,
            new(14000, 14000, ResetAt), new(7000, 7000, NextResetAt)), "ja-JP"));
        var zzz = Text(Present(new ZzzNotesOutcome("123", ObservedAt, new(120, 240, RecoveryAt), new(100, 400, null),
            ScratchCardOutcome.Incomplete, VideoStoreOutcome.WaitingToOpen, new(1, 3, RecoveryAt), new(2, 5, ResetAt),
            new(3, 7, NextResetAt), TempleAdventureOutcome.Dispatchable, TempleCraftingOutcome.Craftable,
            TempleSalesOutcome.NotSelling), "ja-JP"));

        Assert.Multiple(() =>
        {
            Assert.That(hsr[0], Is.EqualTo("## 崩壊：スターレイル リアルタイム便箋（UID：123）"));
            Assert.That(hsr, Does.Contain("### 開拓力").And.Contain("### 歴戦余韻").And.Contain("すべて受取済み！"));
            Assert.That(zzz[0], Is.EqualTo("## ゼンレスゾーンゼロ リアルタイム便箋（UID：123）"));
            Assert.That(zzz, Does.Contain("### バッテリー").And.Contain("### デイリーミッション"));
            Assert.That(zzz.Any(x => x.StartsWith("スクラッチカード/占い：", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void CodesAndCheckIn_AreLocalised()
    {
        var codes = Text(Present(CodeRedemptionOutcome.Create(Game.Genshin, "uid",
            [RedemptionItem.Completed("CODE", RedemptionOutcome.Redeemed)], OperationOutcome.Complete), "de-DE"));
        var checkIn = Text(Present(DailyCheckInOutcome.Create([CheckInItem.Completed(Game.Genshin,
            CheckInOutcome.Succeeded)]), "de-DE"));

        Assert.Multiple(() =>
        {
            Assert.That(codes, Is.EqualTo(new[] { "CODE: Erfolgreich eingelöst!" }));
            Assert.That(checkIn, Is.EqualTo(new[] { "Genshin Impact: Check-in erfolgreich!" }));
        });
    }

    [Test]
    public void ErrorText_MapsCodesToSafeCategoriesAndNeverUsesDomainMessages()
    {
        var localizer = CommandUiLocalizer.Default;
        var error = new CommandError(CommandErrorCode.ApiUnavailable, CommandErrorTarget.RealTimeNotes);

        foreach (var cultureName in SupportedLocaleNames)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var text = CommandOutcomePresenter.ErrorText(error, localizer, culture);

            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo(localizer.Get(culture, CommandUiKeys.ErrorApiUnavailable)));
                Assert.That(text, Is.Not.EqualTo(error.Message));
            });
        }

        Assert.That(CommandOutcomePresenter.ErrorText(error, localizer, CultureInfo.GetCultureInfo("pt-PT")),
            Is.EqualTo(error.Message), "Unsupported cultures must fall back to the neutral English text");
        Assert.That(CommandOutcomePresenter.SafeErrorText(error), Is.EqualTo(error.Message));
    }

    [Test]
    public void UnknownErrorCode_FallsBackToTheSafeUnexpectedCategory()
    {
        var culture = CultureInfo.GetCultureInfo("ja-JP");
        var error = new CommandError((CommandErrorCode)9999, CommandErrorTarget.None);

        Assert.That(CommandOutcomePresenter.ErrorText(error, CommandUiLocalizer.Default, culture),
            Is.EqualTo(CommandUiLocalizer.Default.Get(culture, CommandUiKeys.ErrorUnexpected)));
    }

    [Test]
    public void NeutralOverload_KeepsEnglishBehaviourForEverySupportedLocale()
    {
        var outcomes = new CommandOutcome[]
        {
            RosterOutcome.Empty(Game.Genshin, "uid"),
            CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a"], 4),
            EndgameOutcome.Locked(Game.Genshin, "uid", EndgameMode.StygianOnslaught),
            CodeRedemptionOutcome.Create(Game.Genshin, "uid",
                [RedemptionItem.Completed("CODE", RedemptionOutcome.Expired)])
        };

        foreach (var outcome in outcomes)
        {
            var english = Text(CommandOutcomePresenter.Present(outcome, 42));
            var explicitEnglish = Text(CommandOutcomePresenter.Present(outcome, 42,
                CommandUiLocalizer.Default, BotUiCulture.English));
            var japanese = Text(Present(outcome, "ja-JP"));

            Assert.Multiple(() =>
            {
                Assert.That(explicitEnglish, Is.EqualTo(english));
                Assert.That(japanese, Is.Not.EqualTo(english));
            });
        }
    }

    [Test]
    public void UnsupportedCulture_FallsBackToEnglishText()
    {
        var outcome = CharacterCardsOutcome.LimitExceeded(Game.Genshin, ["a"], 4);

        Assert.That(Text(Present(outcome, "pt-PT")), Is.EqualTo(Text(Present(outcome, "en-US"))));
    }
}
