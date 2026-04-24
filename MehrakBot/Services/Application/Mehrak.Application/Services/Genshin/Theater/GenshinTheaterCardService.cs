#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Theater;

internal class GenshinTheaterCardService : CardServiceBase<GenshinTheaterInformation>
{
    private Image<Rgba32> m_TheaterStarLit = null!;
    private Image<Rgba32> m_TheaterStarUnlit = null!;
    private Image<Rgba32> m_TheaterBuff = null!;

    public GenshinTheaterCardService(IImageRepository imageRepository, ILogger<GenshinTheaterCardService> logger, IApplicationMetrics metrics)
        : base(
            "Genshin Theater",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 40, normalSize: 28, smallSize: null))
    { }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_TheaterStarLit = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("genshin_theater_star", cancellationToken),
            cancellationToken);
        m_TheaterStarUnlit = m_TheaterStarLit.CloneAs<Rgba32>();
        m_TheaterStarUnlit.Mutate(ctx => ctx.Brightness(0.5f));

        m_TheaterBuff = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("genshin_theater_buff", cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("genshin_theater_bg", cancellationToken),
            cancellationToken);
        StaticBackground.Mutate(x => x.Brightness(0.35f));
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<GenshinTheaterInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var theaterData = context.Data;

        var constMap = context.GetParameter<Dictionary<int, int>>("constMap")
            ?? throw new CommandException("constMap parameter is missing for Theater card generation");

        var avatarImages = await theaterData.Detail.RoundsData
            .SelectMany(x => x.Avatars)
            .DistinctBy(x => x.AvatarId)
            .ToAsyncEnumerable()
            .Select(async (y, token) =>
                new GenshinAvatar(y.AvatarId, y.Level, y.Rarity,
                    y.AvatarType == 1 ? constMap[y.AvatarId] : 0,
                    await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(y.ToImageName()), token),
                    y.AvatarType))
            .ToDictionaryAsync(x => x,
                x => x.GetStyledAvatarImage(), GenshinAvatarIdComparer.Instance);
        var buffImages = await theaterData.Detail.RoundsData[0].SplendourBuff!.Buffs
            .ToAsyncEnumerable()
            .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.Name),
                async (x, token) =>
                {
                    var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token);
                    image.Mutate(ctx => ctx.Resize(50, 0));
                    return image;
                });
        ItRankAvatar[] fightStats =
        [
            theaterData.Detail.FightStatistic.MaxDamageAvatar,
            theaterData.Detail.FightStatistic.MaxTakeDamageAvatar, theaterData.Detail.FightStatistic.MaxDefeatAvatar
        ];

        var sideAvatarImages = await fightStats.DistinctBy(x => x.AvatarId).ToAsyncEnumerable()
            .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.AvatarId),
                async (x, token) =>
                {
                    var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token);
                    image.Mutate(ctx => ctx.Resize(100, 0));
                    return image;
                });

        disposables.AddRange(avatarImages.Keys);
        disposables.AddRange(avatarImages.Values);
        disposables.AddRange(buffImages.Values);
        disposables.AddRange(sideAvatarImages.Values);

        var maxRound = GetMaxRound(theaterData.Stat.DifficultyId);

        var height = 590 +
                     (maxRound + 1) / 2 * 300;
        // 1900 x height
        if (height > background.Height)
            background.Mutate(ctx => ctx.Resize(0, height));
        var rectangle = new Rectangle(background.Width / 2 - 1900 / 2,
            background.Height / 2 - height / 2, 1900, height);
        background.Mutate(ctx =>
        {
            ctx.Crop(rectangle);
            ctx.GaussianBlur(10);

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Imaginarium Theater", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(905, 120),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            },
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(theaterData.Schedule.StartTime)).ToString("yyyy/MM"),
                Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 120),
                VerticalAlignment = VerticalAlignment.Bottom
            }, GetDifficultyString(theaterData.Stat.DifficultyId), Color.White);

            ctx.DrawText($"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Fonts.Normal,
                Color.White,
                new PointF(50, 150));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(905, 150),
                HorizontalAlignment = HorizontalAlignment.Right
            }, context.GameProfile.GameUid!, Color.White);

            ctx.DrawRoundedRectangleOverlay(875, 330, new PointF(40, 210),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

            ctx.DrawText("Fantasia Flowers Used", Fonts.Normal, Color.White, new PointF(70, 240));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(885, 240),
                HorizontalAlignment = HorizontalAlignment.Right
            }, theaterData.Stat.CoinNum.ToString(), Color.White);
            ctx.DrawLine(Color.White, 2f, new PointF(70, 290), new PointF(885, 290));

            ctx.DrawText("External Audience Support", Fonts.Normal, Color.White, new PointF(70, 320));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(885, 320),
                HorizontalAlignment = HorizontalAlignment.Right
            }, theaterData.Stat.AvatarBonusNum.ToString(), Color.White);
            ctx.DrawLine(Color.White, 2f, new PointF(70, 370), new PointF(885, 370));

            ctx.DrawText("Supporting Cast Assists", Fonts.Normal, Color.White, new PointF(70, 400));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(885, 400),
                HorizontalAlignment = HorizontalAlignment.Right
            }, theaterData.Stat.RentCnt.ToString(), Color.White);
            ctx.DrawLine(Color.White, 2f, new PointF(70, 450), new PointF(885, 450));

            ctx.DrawText("Total Cast Time", Fonts.Normal, Color.White, new PointF(70, 480));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(885, 480),
                HorizontalAlignment = HorizontalAlignment.Right
            }, TimeSpan.FromSeconds(theaterData.Detail.FightStatistic.TotalUseTime).ToString(@"mm\:ss"),
                Color.White);

            ctx.DrawRoundedRectangleOverlay(875, 360, new PointF(985, 50),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1135, 105),
                VerticalAlignment = VerticalAlignment.Center
            }, "Highest Damage Dealt", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1820, 105),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{theaterData.Detail.FightStatistic.MaxDamageAvatar.Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDamageAvatar.AvatarId],
                new Point(1005, 40), 1f);
            ctx.DrawLine(Color.White, 2f, new PointF(1020, 165), new PointF(1820, 165));

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1135, 225),
                VerticalAlignment = VerticalAlignment.Center
            }, "Most Damage Taken", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1820, 225),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.AvatarId],
                new Point(1005, 160), 1f);
            ctx.DrawLine(Color.White, 2f, new PointF(1020, 285), new PointF(1820, 285));

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1135, 345),
                VerticalAlignment = VerticalAlignment.Center
            }, "Most Opponents Defeated", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1820, 345),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{theaterData.Detail.FightStatistic.MaxDefeatAvatar.Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDefeatAvatar.AvatarId],
                new Point(1005, 280), 1f);

            ctx.DrawRoundedRectangleOverlay(875, 110, new PointF(985, 430),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
            ctx.DrawText("Stellas", Fonts.Normal, Color.White, new PointF(1005, 470));

            var medalOffset = 0;

            if (theaterData.Stat.GetMedalRoundList.Count > 10)
            {
                for (var i = theaterData.Stat.GetMedalRoundList.Count - 1; i >= 10; i--)
                {
                    ctx.DrawImage(theaterData.Stat.GetMedalRoundList[i] == 1
                            ? m_TheaterStarLit
                            : m_TheaterStarUnlit,
                        new Point(1800 - medalOffset, 465), 1f);
                    medalOffset += 45;
                }

                var lineX = 1800 - medalOffset + 30;
                ctx.DrawLine(Color.FromRgba(69, 69, 69, 128), 2f, new PointF(lineX, 450), new PointF(lineX, 525));
                medalOffset += 25;
            }

            for (var i = Math.Min(9, theaterData.Stat.GetMedalRoundList.Count - 1); i >= 0; i--)
            {
                ctx.DrawImage(theaterData.Stat.GetMedalRoundList[i] == 1
                        ? m_TheaterStarLit
                        : m_TheaterStarUnlit,
                    new Point(1800 - medalOffset, 465), 1f);
                medalOffset += 45;
            }

            var hasFastest = false;
            var levelIndex = 1;
            var arcanaLeft = 2;
            bool[] arcanaCleared = [false, false];

            var alternateLookup =
                avatarImages.GetAlternateLookup<int>();

            var roundsData = GetCleanedRoundsData(theaterData);

            for (var i = 0; i < maxRound; i++)
            {
                var xOffset = i == maxRound - 1 &&
                              maxRound % 2 == 1
                    ? 512
                    : 50 + (i % 2 == 0 ? 0 : 945);
                var yOffset = 660 + i / 2 * 300;
                var roundData = i < roundsData.Count
                    ? roundsData[i]
                    : null;

                ctx.DrawRoundedRectangleOverlay(670, 270, new PointF(xOffset - 10, yOffset - 70),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                ctx.DrawRoundedRectangleOverlay(195, 270, new PointF(xOffset + 670, yOffset - 70),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                if (roundData == null)
                {
                    if (i < 12 - arcanaLeft)
                    {
                        levelIndex++;
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(xOffset + 15, yOffset - 45)
                        }, GetActName(levelIndex, false), Color.White);
                    }
                    else
                    {
                        var firstArcana = Array.IndexOf(arcanaCleared, false);
                        arcanaCleared[firstArcana] = true;
                        ctx.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(xOffset + 15, yOffset - 45)
                        }, GetActName(firstArcana + 1, true), Color.White);
                        arcanaLeft--;
                    }

                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(xOffset + 335, yOffset + 80),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "No Clear Records", Color.White);

                    ctx.DrawLine(new SolidPen(new PenOptions(Color.White, 5f) { EndCapStyle = EndCapStyle.Round }),
                        new PointF(xOffset + 690, yOffset + 180),
                        new PointF(xOffset + 845, yOffset - 50));
                    continue;
                }

                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 15, yOffset - 45)
                },
                    GetActName(roundData.IsTarot ? roundData.TarotSerialNumber : roundData.RoundId,
                        roundData.IsTarot),
                    Color.White);
                if (roundData.IsTarot)
                {
                    arcanaLeft--;
                    arcanaCleared[roundData.TarotSerialNumber - 1] = true;
                }
                else
                {
                    levelIndex = roundData.RoundId;
                }

                var roster = RosterImageBuilder.Build(
                    roundData.Avatars.Select(x => alternateLookup[x.AvatarId]),
                    new RosterLayout(MaxSlots: 4));
                disposables.Add(roster);
                ctx.DrawImage(roster, new Point(xOffset, yOffset), 1f);
                ctx.DrawImage(roundData.IsGetMedal ? m_TheaterStarLit : m_TheaterStarUnlit,
                    new Point(xOffset + 600, yOffset - 55), 1f);

                ctx.DrawImage(m_TheaterBuff, new Point(xOffset + 690, yOffset - 60), 1f);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 765, yOffset - 45)
                }, $"Lv. {roundData.SplendourBuff!.Summary.TotalLevel}", Color.White);

                ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 680, yOffset),
                    new PointF(xOffset + 850, yOffset));

                for (var j = 0; j < roundData.SplendourBuff!.Buffs.Count; j++)
                {
                    var buff = roundData.SplendourBuff.Buffs[j];
                    var buffImage = buffImages[buff.Name];
                    var y = yOffset + 20 + j * 55;
                    ctx.DrawImage(buffImage, new Point(xOffset + 690, y), 1f);

                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(xOffset + 765, y + 10)
                    }, $"Lv. {buff.Level}", buff.Level > 0 ? Color.White : Color.Gray);
                }

                if (!hasFastest && roundData.Avatars.Select(x => x.AvatarId).SequenceEqual(
                        theaterData.Detail.FightStatistic.ShortestAvatarList.Select(x => x.AvatarId
                        )))
                {
                    hasFastest = true;
                    ctx.DrawRoundedRectangleOverlay(190, 50, new PointF(xOffset + 400, yOffset - 60),
                        new RoundedRectangleOverlayStyle(Color.FromRgba(128, 128, 128, 128), CornerRadius: 15));
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(xOffset + 412, yOffset - 45)
                    }, "Fastest Act", Color.Gold);
                }
            }
        });
    }

    private static string GetDifficultyString(int difficulty)
    {
        return difficulty switch
        {
            1 => "Easy Mode",
            2 => "Normal Mode",
            3 => "Hard Mode",
            4 => "Visionary Mode",
            5 => "Lunar Mode",
            _ => throw new ArgumentException("Difficulty must be between 1 and 5", nameof(difficulty))
        };
    }

    private static int GetMaxRound(int difficulty)
    {
        return difficulty switch
        {
            1 => 3,
            2 => 6,
            3 => 8,
            4 => 10,
            5 => 12,
            _ => throw new ArgumentException("Difficulty must be between 1 and 5", nameof(difficulty))
        };
    }

    private static string GetActName(int floorNumber, bool isArcana)
    {
        return isArcana switch
        {
            true => $"Arcana {floorNumber}",
            false => $"Act {floorNumber}"
        };
    }

    private static List<RoundsData> GetCleanedRoundsData(GenshinTheaterInformation theaterData)
    {
        var roundsData = theaterData.Detail.RoundsData;
        var medalRoundList = theaterData.Stat.GetMedalRoundList;

        Dictionary<int, RoundsData> selectedArcanaRounds = [];
        for (var i = roundsData.Count - 1; i >= 0; i--)
        {
            var roundData = roundsData[i];
            if (!roundData.IsTarot || roundData.TarotSerialNumber is < 1 or > 2 || selectedArcanaRounds.ContainsKey(roundData.TarotSerialNumber))
                continue;

            var medalIndex = roundData.TarotSerialNumber + 9;
            if (medalRoundList.Count <= medalIndex)
                continue;

            var expectedMedal = medalRoundList[medalIndex] == 1;
            if (roundData.IsGetMedal != expectedMedal)
                continue;

            selectedArcanaRounds[roundData.TarotSerialNumber] = roundData;
        }

        return [.. roundsData.Where(x => !x.IsTarot || selectedArcanaRounds.TryGetValue(x.TarotSerialNumber, out var roundData) && ReferenceEquals(x, roundData))];
    }
}
