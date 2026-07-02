#region

using System.Numerics;
using Mehrak.Application.Genshin;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.User.Abstractions;
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
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.TheaterStarName, cancellationToken),
            cancellationToken);
        m_TheaterStarUnlit = m_TheaterStarLit.CloneAs<Rgba32>();
        m_TheaterStarUnlit.Mutate(ctx => ctx.Brightness(0.5f));

        m_TheaterBuff = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.TheaterBuffName, cancellationToken),
            cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.TheaterBackgroundName, cancellationToken),
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

        var avatarRoundItems = theaterData.Detail.RoundsData
            .SelectMany(x => x.Avatars)
            .DistinctBy(x => x.AvatarId)
            .ToList();

        var avatarTasks = avatarRoundItems.Select(async x =>
        {
            await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken);
            var image = await Image.LoadAsync(stream, cancellationToken);
            var avatar = new GenshinAvatar(x.AvatarId, x.Level, x.Rarity,
                x.AvatarType == 1 ? constMap[x.AvatarId] : 0, image, x.AvatarType);
            disposables.Add(avatar);
            return avatar;
        }).ToList();

        var avatarImages = (await Task.WhenAll(avatarTasks))
            .ToDictionary(x => x, x => x, GenshinAvatarIdComparer.Instance);

        var buffList = theaterData.Detail.RoundsData[0].SplendourBuff!.Buffs.ToList();

        var buffTasks = buffList.Select(async x =>
        {
            var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
            image.Mutate(ctx => ctx.Resize(50, 0));
            return (x.Name, Image: image);
        }).ToList();

        var buffImages = (await Task.WhenAll(buffTasks))
            .ToDictionary(x => x.Name, x => x.Image);

        ItRankAvatar[] fightStats =
        [
            theaterData.Detail.FightStatistic.MaxDamageAvatar,
            theaterData.Detail.FightStatistic.MaxTakeDamageAvatar, theaterData.Detail.FightStatistic.MaxDefeatAvatar
        ];

        var sideAvatarList = fightStats.DistinctBy(x => x.AvatarId).ToList();

        var sideAvatarTasks = sideAvatarList.Select(async x =>
        {
            var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken);
            image.Mutate(ctx => ctx.Resize(100, 0));
            return (x.AvatarId, Image: image);
        }).ToList();

        var sideAvatarImages = (await Task.WhenAll(sideAvatarTasks))
            .ToDictionary(x => x.AvatarId, x => x.Image);

        var maxRound = GetMaxRound(theaterData.Stat.DifficultyId);

        var height = 620 +
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

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Imaginarium Theater", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(905, 120),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    DateTimeOffset.FromUnixTimeSeconds(long.Parse(theaterData.Schedule.StartTime)).ToString("yyyy/MM"),
                    Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 120),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, GetDifficultyString(theaterData.Stat.DifficultyId), Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(50, 150)
                }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}",
                    Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(905, 150),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.GameProfile.GameUid!, Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(875, 330, new PointF(40, 210),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(70, 240)
                }, "Fantasia Flowers Used", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(885, 240),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.CoinNum.ToString(), Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(70, 290), new PointF(885, 290)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(70, 320)
                }, "External Audience Support", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(885, 320),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.AvatarBonusNum.ToString(), Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(70, 370), new PointF(885, 370)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(70, 400)
                }, "Supporting Cast Assists", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(885, 400),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.RentCnt.ToString(), Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(70, 450), new PointF(885, 450)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(70, 480)
                }, "Total Cast Time", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(885, 480),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, TimeSpan.FromSeconds(theaterData.Detail.FightStatistic.TotalUseTime).ToString(@"mm\:ss"),
                    Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(875, 360, new PointF(985, 50),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1135, 105),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Highest Damage Dealt", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1820, 105),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxDamageAvatar.Value}", Brushes.Solid(Color.White), null);
                var maxDamageImage = sideAvatarImages[theaterData.Detail.FightStatistic.MaxDamageAvatar.AvatarId];
                canvas.DrawImage(maxDamageImage, maxDamageImage.Bounds,
                    new RectangleF(1005, 40, maxDamageImage.Width, maxDamageImage.Height), KnownResamplers.Bicubic);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(1020, 165), new PointF(1820, 165)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1135, 225),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1820, 225),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.Value}", Brushes.Solid(Color.White), null);
                var maxTakeDamageImage = sideAvatarImages[theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.AvatarId];
                canvas.DrawImage(maxTakeDamageImage, maxTakeDamageImage.Bounds,
                    new RectangleF(1005, 160, maxTakeDamageImage.Width, maxTakeDamageImage.Height), KnownResamplers.Bicubic);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(1020, 285), new PointF(1820, 285)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1135, 345),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Opponents Defeated", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1820, 345),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxDefeatAvatar.Value}", Brushes.Solid(Color.White), null);
                var maxDefeatImage = sideAvatarImages[theaterData.Detail.FightStatistic.MaxDefeatAvatar.AvatarId];
                canvas.DrawImage(maxDefeatImage, maxDefeatImage.Bounds,
                    new RectangleF(1005, 280, maxDefeatImage.Width, maxDefeatImage.Height), KnownResamplers.Bicubic);

                canvas.DrawRoundedRectangleOverlay(875, 110, new PointF(985, 430),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(1005, 470)
                }, "Stellas", Brushes.Solid(Color.White), null);

                var medalOffset = 0;

                if (theaterData.Stat.GetMedalRoundList.Count > 10)
                {
                    for (var i = theaterData.Stat.GetMedalRoundList.Count - 1; i >= 10; i--)
                    {
                        var starImage = theaterData.Stat.GetMedalRoundList[i] == 1
                            ? m_TheaterStarLit
                            : m_TheaterStarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(1800 - medalOffset, 465, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
                        medalOffset += 45;
                    }

                    var lineX = 1800 - medalOffset + 30;
                    canvas.Draw(Pens.Solid(Color.FromPixel(new Rgba32(69, 69, 69, 128)), 2f),
                        new PathBuilder().AddLine(new PointF(lineX, 450), new PointF(lineX, 525)).Build());
                    medalOffset += 25;
                }

                for (var i = Math.Min(9, theaterData.Stat.GetMedalRoundList.Count - 1); i >= 0; i--)
                {
                    var starImage = theaterData.Stat.GetMedalRoundList[i] == 1
                        ? m_TheaterStarLit
                        : m_TheaterStarUnlit;
                    canvas.DrawImage(starImage, starImage.Bounds,
                        new RectangleF(1800 - medalOffset, 465, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
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

                    canvas.DrawRoundedRectangleOverlay(670, 270, new PointF(xOffset - 10, yOffset - 70),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                    canvas.DrawRoundedRectangleOverlay(195, 270, new PointF(xOffset + 670, yOffset - 70),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                    if (roundData == null)
                    {
                        if (i < 12 - arcanaLeft)
                        {
                            levelIndex++;
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new Vector2(xOffset + 15, yOffset - 45)
                            }, GetActName(levelIndex, false), Brushes.Solid(Color.White), null);
                        }
                        else
                        {
                            var firstArcana = Array.IndexOf(arcanaCleared, false);
                            arcanaCleared[firstArcana] = true;
                            canvas.DrawText(new RichTextOptions(Fonts.Normal)
                            {
                                Origin = new Vector2(xOffset + 15, yOffset - 45)
                            }, GetActName(firstArcana + 1, true), Brushes.Solid(Color.White), null);
                            arcanaLeft--;
                        }

                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(xOffset + 335, yOffset + 80),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, "No Clear Records", Brushes.Solid(Color.White), null);

                        canvas.Draw(Pens.Solid(Color.White, 5f),
                            new PathBuilder().AddLine(
                                new PointF(xOffset + 690, yOffset + 180),
                                new PointF(xOffset + 845, yOffset - 50)).Build());
                        continue;
                    }

                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(xOffset + 15, yOffset - 45)
                    },
                        GetActName(roundData.IsTarot ? roundData.TarotSerialNumber : roundData.RoundId,
                            roundData.IsTarot),
                        Brushes.Solid(Color.White), null);
                    if (roundData.IsTarot)
                    {
                        arcanaLeft--;
                        arcanaCleared[roundData.TarotSerialNumber - 1] = true;
                    }
                    else
                    {
                        levelIndex = roundData.RoundId;
                    }

                    RosterImageBuilder.Draw(
                        roundData.Avatars.Select(x => alternateLookup[x.AvatarId]),
                        new RosterLayout(MaxSlots: 4),
                        new Point(xOffset, yOffset),
                        (point, avatar) => avatar.DrawStyledAvatarImage(canvas, point));
                    var medalStarImage = roundData.IsGetMedal ? m_TheaterStarLit : m_TheaterStarUnlit;
                    canvas.DrawImage(medalStarImage, medalStarImage.Bounds,
                        new RectangleF(xOffset + 600, yOffset - 55, medalStarImage.Width, medalStarImage.Height), KnownResamplers.Bicubic);

                    canvas.DrawImage(m_TheaterBuff, m_TheaterBuff.Bounds,
                        new RectangleF(xOffset + 690, yOffset - 60, m_TheaterBuff.Width, m_TheaterBuff.Height), KnownResamplers.Bicubic);
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(xOffset + 765, yOffset - 45)
                    }, $"Lv. {roundData.SplendourBuff!.Summary.TotalLevel}", Brushes.Solid(Color.White), null);

                    canvas.Draw(Pens.Solid(Color.White, 2f),
                        new PathBuilder().AddLine(new PointF(xOffset + 680, yOffset),
                            new PointF(xOffset + 850, yOffset)).Build());

                    for (var j = 0; j < roundData.SplendourBuff!.Buffs.Count; j++)
                    {
                        var buff = roundData.SplendourBuff.Buffs[j];
                        var buffImage = buffImages[buff.Name];
                        var y = yOffset + 20 + j * 55;
                        canvas.DrawImage(buffImage, buffImage.Bounds,
                            new RectangleF(xOffset + 690, y, buffImage.Width, buffImage.Height), KnownResamplers.Bicubic);

                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(xOffset + 765, y + 10)
                        }, $"Lv. {buff.Level}", Brushes.Solid(buff.Level > 0 ? Color.White : Color.Gray), null);
                    }

                    if (!hasFastest && roundData.Avatars.Select(x => x.AvatarId).SequenceEqual(
                            theaterData.Detail.FightStatistic.ShortestAvatarList.Select(x => x.AvatarId
                            )))
                    {
                        hasFastest = true;
                        canvas.DrawRoundedRectangleOverlay(190, 50, new PointF(xOffset + 400, yOffset - 60),
                            new RoundedRectangleOverlayStyle(Color.FromPixel(new Rgba32(128, 128, 128, 128)), CornerRadius: 15));
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(xOffset + 412, yOffset - 45)
                        }, "Fastest Act", Brushes.Solid(Color.Gold), null);
                    }
                }

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(rectangle.Width - 20, rectangle.Height - 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                }
                );
            });
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
