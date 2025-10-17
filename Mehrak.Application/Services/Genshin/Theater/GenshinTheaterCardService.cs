#region

using Mehrak.Application.Models;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

#endregion

namespace Mehrak.Application.Services.Genshin.Theater;

internal class GenshinTheaterCardService :
    ICardService<GenshinEndGameGenerationContext<GenshinTheaterInformation>, GenshinTheaterInformation>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<GenshinTheaterCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private Image m_Background = null!;
    private Image m_TheaterStarLit = null!;
    private Image m_TheaterStarUnlit = null!;
    private Image m_TheaterBuff = null!;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public GenshinTheaterCardService(IImageRepository imageRepository, ILogger<GenshinTheaterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_TheaterStarLit = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_theater_star"), cancellationToken);
        m_TheaterStarUnlit = m_TheaterStarLit.CloneAs<Rgba32>();
        m_TheaterStarUnlit.Mutate(ctx => ctx.Brightness(0.5f));

        m_TheaterBuff = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_theater_buff"), cancellationToken);
        m_Background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_theater_bg"), cancellationToken);
        m_Background.Mutate(x => x.Brightness(0.35f));
    }

    public async Task<Stream> GetCardAsync(GenshinEndGameGenerationContext<GenshinTheaterInformation> context)
    {
        var theaterData = context.Data;
        List<IDisposable> disposableResources = [];
        try
        {
            Dictionary<GenshinAvatar, Image<Rgba32>> avatarImages = await theaterData.Detail.RoundsData.SelectMany(x =>
                    x.Avatars)
                .DistinctBy(x => x.AvatarId)
                .ToAsyncEnumerable()
                .SelectAwait(async y =>
                    new GenshinAvatar(y.AvatarId, y.Level, y.Rarity, y.AvatarType == 1 ? context.ConstMap[y.AvatarId] : 0,
                        await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.AvatarName, y.AvatarId))),
                        y.AvatarType))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()), GenshinAvatarIdComparer.Instance);
            Dictionary<string, Image> buffImages = await theaterData.Detail.RoundsData[0].SplendourBuff!.Buffs.ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Name),
                    async x =>
                    {
                        Image image = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync($"zzz_theater_buff_{x.Name.Replace(' ', '_')}"));
                        image.Mutate(ctx => ctx.Resize(50, 0));
                        return image;
                    });
            ItRankAvatar[] fightStats =
            [
                theaterData.Detail.FightStatistic.MaxDamageAvatar,
                theaterData.Detail.FightStatistic.MaxTakeDamageAvatar, theaterData.Detail.FightStatistic.MaxDefeatAvatar
            ];

            Dictionary<int, Image> sideAvatarImages = await fightStats.DistinctBy(x => x.AvatarId).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.AvatarId),
                    async x =>
                    {
                        Image image = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.SideAvatarName, x.AvatarId)));
                        image.Mutate(ctx => ctx.Resize(100, 0));
                        return image;
                    });

            disposableResources.AddRange(avatarImages.Keys);
            disposableResources.AddRange(avatarImages.Values);
            disposableResources.AddRange(buffImages.Values);
            disposableResources.AddRange(sideAvatarImages.Values);

            int maxRound = GetMaxRound(theaterData.Stat.DifficultyId);

            int height = 590 +
                         ((maxRound + 1) / 2 * 300);
            // 1900 x height
            Image<Rgba32> background = m_Background.CloneAs<Rgba32>();
            disposableResources.Add(background);

            background.Mutate(ctx =>
            {
                if (height > background.Height)
                    ctx.Resize(0, height);
                Rectangle rectangle = new((ctx.GetCurrentSize().Width / 2) - (1900 / 2),
                    (ctx.GetCurrentSize().Height / 2) - (height / 2), 1900, height);
                ctx.Crop(rectangle);
                ctx.GaussianBlur(10);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Imaginarium Theater", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(905, 120),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    DateTimeOffset.FromUnixTimeSeconds(long.Parse(theaterData.Schedule.StartTime)).ToString("yyyy/MM"),
                    Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 120),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, GetDifficultyString(theaterData.Stat.DifficultyId), Color.White);

                ctx.DrawText($"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", m_NormalFont, Color.White,
                    new PointF(50, 150));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(905, 150),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.GameProfile.GameUid!, Color.White);

                IPath statsBackground = ImageUtility.CreateRoundedRectanglePath(875, 330, 15).Translate(40, 210);
                ctx.Fill(OverlayColor, statsBackground);

                ctx.DrawText("Fantasia Flowers Used", m_NormalFont, Color.White, new PointF(70, 240));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(885, 240),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.CoinNum.ToString(), Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(70, 290), new PointF(885, 290));

                ctx.DrawText("External Audience Support", m_NormalFont, Color.White, new PointF(70, 320));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(885, 320),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.AvatarBonusNum.ToString(), Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(70, 370), new PointF(885, 370));

                ctx.DrawText("Supporting Cast Assists", m_NormalFont, Color.White, new PointF(70, 400));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(885, 400),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Stat.RentCnt.ToString(), Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(70, 450), new PointF(885, 450));

                ctx.DrawText("Total Cast Time", m_NormalFont, Color.White, new PointF(70, 480));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(885, 480),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, TimeSpan.FromSeconds(theaterData.Detail.FightStatistic.TotalUseTime).ToString(@"mm\:ss"),
                    Color.White);

                IPath statBackground = ImageUtility.CreateRoundedRectanglePath(875, 360, 15).Translate(985, 50);
                ctx.Fill(OverlayColor, statBackground);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 105),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Highest Damage Dealt", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1820, 105),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxDamageAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDamageAvatar.AvatarId],
                    new Point(1005, 40), 1f);
                ctx.DrawLine(Color.White, 2f, new PointF(1020, 165), new PointF(1820, 165));

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 225),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1820, 225),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.AvatarId],
                    new Point(1005, 160), 1f);
                ctx.DrawLine(Color.White, 2f, new PointF(1020, 285), new PointF(1820, 285));

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 345),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Opponents Defeated", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1820, 345),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{theaterData.Detail.FightStatistic.MaxDefeatAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDefeatAvatar.AvatarId],
                    new Point(1005, 280), 1f);

                IPath starBackground = ImageUtility.CreateRoundedRectanglePath(875, 110, 15).Translate(985, 430);
                ctx.Fill(OverlayColor, starBackground);
                ctx.DrawText("Stellas", m_NormalFont, Color.White, new PointF(1005, 470));

                int medalOffset = 0;

                if (theaterData.Stat.GetMedalRoundList.Count > 10)
                {
                    for (int i = theaterData.Stat.GetMedalRoundList.Count - 1; i >= 10; i--)
                    {
                        ctx.DrawImage(theaterData.Stat.GetMedalRoundList[i] == 1
                                ? m_TheaterStarLit
                                : m_TheaterStarUnlit,
                            new Point(1800 - medalOffset, 465), 1f);
                        medalOffset += 45;
                    }
                    int lineX = 1800 - medalOffset + 30;
                    ctx.DrawLine(Color.FromRgba(69, 69, 69, 128), 2f, new PointF(lineX, 450), new PointF(lineX, 525));
                    medalOffset += 25;
                }

                for (int i = Math.Min(9, theaterData.Stat.GetMedalRoundList.Count - 1); i >= 0; i--)
                {
                    ctx.DrawImage(theaterData.Stat.GetMedalRoundList[i] == 1
                            ? m_TheaterStarLit
                            : m_TheaterStarUnlit,
                        new Point(1800 - medalOffset, 465), 1f);
                    medalOffset += 45;
                }

                bool hasFastest = false;
                int levelIndex = 1;
                int arcanaLeft = 2;
                bool[] arcanaCleared = [false, false];

                Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> alternateLookup = avatarImages.GetAlternateLookup<int>();

                for (int i = 0; i < maxRound; i++)
                {
                    int xOffset = i == maxRound - 1 &&
                                  maxRound % 2 == 1
                        ? 512
                        : 50 + (i % 2 == 0 ? 0 : 945);
                    int yOffset = 660 + (i / 2 * 300);
                    RoundsData? roundData = i < theaterData.Detail.RoundsData.Count ? theaterData.Detail.RoundsData[i] : null;

                    IPath avatarBackground = ImageUtility.CreateRoundedRectanglePath(670, 270, 15);
                    ctx.Fill(OverlayColor, avatarBackground.Translate(xOffset - 10, yOffset - 70));

                    IPath buffBackground = ImageUtility.CreateRoundedRectanglePath(195, 270, 15);
                    ctx.Fill(OverlayColor, buffBackground.Translate(xOffset + 670, yOffset - 70));

                    if (roundData == null)
                    {
                        if (i < 12 - arcanaLeft)
                        {
                            levelIndex++;
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 15, yOffset - 45)
                            }, GetActName(levelIndex, false), Color.White);
                        }
                        else
                        {
                            int firstArcana = Array.IndexOf(arcanaCleared, false);
                            arcanaCleared[firstArcana] = true;
                            ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 15, yOffset - 45)
                            }, GetActName(firstArcana + 1, true), Color.White);
                            arcanaLeft--;
                        }

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
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

                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 15, yOffset - 45)
                    }, GetActName(roundData.IsTarot ? roundData.TarotSerialNumber : roundData.RoundId, roundData.IsTarot),
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

                    ctx.DrawImage(GetRosterImage(roundData.Avatars.Select(x => x.AvatarId), alternateLookup),
                            new Point(xOffset, yOffset), 1f);
                    ctx.DrawImage(roundData.IsGetMedal ? m_TheaterStarLit : m_TheaterStarUnlit,
                        new Point(xOffset + 600, yOffset - 55), 1f);

                    ctx.DrawImage(m_TheaterBuff, new Point(xOffset + 690, yOffset - 60), 1f);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(xOffset + 765, yOffset - 45)
                    }, $"Lv. {roundData.SplendourBuff!.Summary.TotalLevel}", Color.White);

                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 680, yOffset),
                        new PointF(xOffset + 850, yOffset));

                    for (int j = 0; j < roundData.SplendourBuff!.Buffs.Count; j++)
                    {
                        Buff buff = roundData.SplendourBuff.Buffs[j];
                        Image buffImage = buffImages[buff.Name];
                        int y = yOffset + 20 + (j * 55);
                        ctx.DrawImage(buffImage, new Point(xOffset + 690, y), 1f);

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 765, y + 10)
                        }, $"Lv. {buff.Level}", buff.Level > 0 ? Color.White : Color.Gray);
                    }

                    if (!hasFastest && roundData.Avatars.Select(x => x.AvatarId).SequenceEqual(
                            theaterData.Detail.FightStatistic.ShortestAvatarList.Select(x => x.AvatarId
                            )))
                    {
                        hasFastest = true;
                        IPath fastestBackground = ImageUtility.CreateRoundedRectanglePath(190, 50, 15);
                        ctx.Fill(Color.FromRgba(128, 128, 128, 128),
                            fastestBackground.Translate(xOffset + 400, yOffset - 60));
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 412, yOffset - 45)
                        }, "Fastest Act", Color.Gold);
                    }
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to generate theater card for uid {GameUid}", context.GameProfile.GameUid);
            throw new CommandException("An error occurred while generating Imaginarium Theater card", e);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private static Image<Rgba32> GetRosterImage(IEnumerable<int> ids,
        Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> imageDict)
    {
        const int avatarWidth = 150;

        List<int> avatarIds = [.. ids];
        int offset = ((4 - avatarIds.Count) * avatarWidth / 2) + 10;

        Image<Rgba32> rosterImage = new(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + (i * (avatarWidth + 10));
                ctx.DrawImage(imageDict[avatarIds[i]], new Point(x, 0), 1f);
            }
        });

        return rosterImage;
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
}
