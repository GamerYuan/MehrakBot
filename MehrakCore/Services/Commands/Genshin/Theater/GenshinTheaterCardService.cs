#region

using System.Numerics;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Theater;

internal class GenshinTheaterCardService : ICommandService<GenshinTheaterCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinTheaterCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private readonly Image m_TheaterStarLit;
    private readonly Image m_TheaterStarUnlit;
    private readonly Image m_TheaterBuff;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public GenshinTheaterCardService(ImageRepository imageRepository, ILogger<GenshinTheaterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_TheaterStarLit = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("genshin_theater_star").Result);
        m_TheaterStarUnlit = m_TheaterStarLit.CloneAs<Rgba32>();
        m_TheaterStarUnlit.Mutate(ctx => ctx.Brightness(0.5f));

        m_TheaterBuff = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("genshin_theater_buff").Result);
    }

    public async Task<Stream> GetTheaterCardAsync(GenshinTheaterInformation theaterData, UserGameData userGameData,
        Dictionary<int, int> constMap, Dictionary<string, Stream> buffMap)
    {
        List<IDisposable> disposableResources = [];
        try
        {
            var avatarDict = theaterData.Detail.RoundsData.SelectMany(x => x.Avatars).DistinctBy(x => x.AvatarId)
                .ToDictionary(x => x.AvatarId, x => x);
            var avatarImages = await avatarDict.ToAsyncEnumerable().ToDictionaryAwaitAsync(
                async x => await Task.FromResult(x.Key),
                async x => x.Value.GetStyledAvatarImage(
                    await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_avatar_{x.Value.AvatarId}")),
                    x.Value.AvatarType, x.Value.AvatarType != 1 ? 0 : constMap[x.Value.AvatarId]));
            var buffImages = await buffMap.ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Key),
                    async x =>
                    {
                        var image = await Image.LoadAsync(x.Value);
                        image.Mutate(ctx => ctx.Resize(50, 0));
                        return image;
                    });
            ItRankAvatar[] fightStats =
            [
                theaterData.Detail.FightStatistic.MaxDamageAvatar,
                theaterData.Detail.FightStatistic.MaxTakeDamageAvatar, theaterData.Detail.FightStatistic.MaxDefeatAvatar
            ];

            var sideAvatarImages = await fightStats.DistinctBy(x => x.AvatarId).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.AvatarId),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_side_avatar_{x.AvatarId}")));

            disposableResources.AddRange(avatarImages.Values);
            disposableResources.AddRange(buffImages.Values);
            disposableResources.AddRange(sideAvatarImages.Values);

            var background = new Image<Rgba32>(1900, 2100);
            disposableResources.Add(background);

            background.Mutate(ctx =>
            {
                ctx.Clear(Color.RebeccaPurple);

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

                ctx.DrawText($"{userGameData.Nickname}·AR {userGameData.Level}", m_NormalFont, Color.White,
                    new PointF(50, 150));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(905, 150),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, userGameData.GameUid!, Color.White);

                var statsBackground = ImageExtensions.CreateRoundedRectanglePath(875, 330, 15).Translate(40, 210);
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
                ctx.DrawLine(Color.White, 2f, new PointF(80, 450), new PointF(885, 450));

                ctx.DrawText("Total Cast Time", m_NormalFont, Color.White, new PointF(70, 480));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(885, 480),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, theaterData.Detail.FightStatistic.TotalUseTime.ToString(), Color.White);

                var overlay = ImageExtensions.CreateRoundedRectanglePath(875, 150, 15).Translate(985, 50);

                for (int i = 0; i < 3; i++)
                {
                    ctx.Fill(OverlayColor, overlay);
                    overlay = overlay.Translate(0, 170);
                }

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 125),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Highest Damage Dealt", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1830, 125),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{theaterData.Detail.FightStatistic.MaxDamageAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDamageAvatar.AvatarId],
                    new Point(985, 30), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 295),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1830, 295),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxTakeDamageAvatar.AvatarId],
                    new Point(985, 200), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1135, 465),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Opponents Defeated", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1830, 465),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{theaterData.Detail.FightStatistic.MaxDefeatAvatar.Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[theaterData.Detail.FightStatistic.MaxDefeatAvatar.AvatarId],
                    new Point(985, 370), 1f);

                for (int i = 0; i < theaterData.Detail.RoundsData.Count; i++)
                {
                    int xOffset = 50 + (i % 2 == 0 ? 0 : 945);
                    int yOffset = 660 + i / 2 * 300;
                    var roundData = theaterData.Detail.RoundsData[i];

                    var avatarBackground = ImageExtensions.CreateRoundedRectanglePath(670, 270, 15);
                    ctx.Fill(OverlayColor, avatarBackground.Translate(xOffset - 10, yOffset - 70));

                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 15, yOffset - 45)
                        }, $"Act {roundData.RoundId}", Color.White);

                    ctx.DrawImage(GetRosterImage(roundData.Avatars.Select(x => x.AvatarId), avatarImages),
                        new Point(xOffset, yOffset), 1f);
                    ctx.DrawImage(roundData.IsGetMedal ? m_TheaterStarLit : m_TheaterStarUnlit,
                        new Point(xOffset + 600, yOffset - 55), 1f);

                    var buffBackground = ImageExtensions.CreateRoundedRectanglePath(195, 270, 15);
                    ctx.Fill(OverlayColor, buffBackground.Translate(xOffset + 670, yOffset - 70));

                    ctx.DrawImage(m_TheaterBuff, new Point(xOffset + 690, yOffset - 60), 1f);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(xOffset + 765, yOffset - 45)
                        }, $"Lv. {roundData.SplendourBuff!.Summary.TotalLevel}", Color.White);

                    ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 680, yOffset),
                        new PointF(xOffset + 850, yOffset - 5));

                    for (int j = 0; j < roundData.SplendourBuff!.Buffs.Count; j++)
                    {
                        var buff = roundData.SplendourBuff.Buffs[j];
                        var buffImage = buffImages[buff.Name];
                        var y = yOffset + 20 + j * 55;
                        ctx.DrawImage(buffImage, new Point(xOffset + 690, y), 1f);

                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                            {
                                Origin = new Vector2(xOffset + 765, y + 10)
                            }, $"Lv. {buff.Level}", Color.White);
                    }
                }
            });

            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to generate theater card for uid {GameUid}", userGameData.GameUid);
            throw new CommandException("An error occurred while generating Imaginarium Theater card", e);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
    }

    private static Image<Rgba32> GetRosterImage(IEnumerable<int> ids, Dictionary<int, Image<Rgba32>> imageDict)
    {
        const int avatarWidth = 150;

        var avatarIds = ids.ToList();
        int offset = (4 - avatarIds.Count) * avatarWidth / 2 + 10;

        var rosterImage = new Image<Rgba32>(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + i * (avatarWidth + 10);
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
            _ => throw new ArgumentException("Difficulty must be between 1 and 4", nameof(difficulty))
        };
    }
}
