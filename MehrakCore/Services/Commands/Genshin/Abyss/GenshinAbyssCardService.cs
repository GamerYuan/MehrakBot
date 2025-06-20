#region

using System.Numerics;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
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

namespace MehrakCore.Services.Commands.Genshin.Abyss;

internal class GenshinAbyssCardService : ICommandService<GenshinAbyssCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinAbyssCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private readonly Image m_AbyssStarIconLit;
    private readonly Image m_AbyssStarIconUnlit;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public GenshinAbyssCardService(ImageRepository imageRepository, ILogger<GenshinAbyssCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_AbyssStarIconLit = Image.LoadAsync(m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss_stars").Result)
            .Result;
        m_AbyssStarIconUnlit = m_AbyssStarIconLit.CloneAs<Rgba32>();
        m_AbyssStarIconUnlit.Mutate(ctx => ctx.Brightness(0.5f));
    }

    public async ValueTask<Stream> GetAbyssCardAsync(uint floor, UserGameData gameData,
        GenshinAbyssInformation abyssData, Dictionary<int, int> constMap)
    {
        List<IDisposable> disposableResources = [];
        try
        {
            var portraitImages = await abyssData.Floors!.SelectMany(x => x.Levels!.SelectMany(y => y.Battles!))
                .SelectMany(x => x.Avatars!).DistinctBy(x => x.Id).ToAsyncEnumerable().ToDictionaryAwaitAsync(
                    async x => await Task.FromResult(x),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_avatar_{x.Id!.Value}")));
            var imageDict = portraitImages.ToDictionary(x => x.Key.Id!.Value,
                x => x.Key.GetStyledAvatarImage(x.Value, constMap[x.Key.Id!.Value]));
            var sideAvatarImages = await abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
                .Concat(abyssData.EnergySkillRank!)
                .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
                .ToAsyncEnumerable().ToDictionaryAwaitAsync(
                    async x => await Task.FromResult(x.AvatarId!.Value),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_side_avatar_{x.AvatarId!.Value}")));
            disposableResources.AddRange(portraitImages.Values);
            disposableResources.AddRange(imageDict.Values);
            disposableResources.AddRange(sideAvatarImages.Values);
            var background =
                await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss_bg"));
            disposableResources.Add(background);
            var floorData = abyssData.Floors!.First(x => x.Index == floor);

            background.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Spiral Abyss", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(750, 80),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.StartTime!)):d} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.EndTime!)):d}",
                    Color.White);

                ctx.DrawText($"{gameData.Nickname}·AR {gameData.Level}", m_NormalFont, Color.White,
                    new PointF(50, 110));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(750, 110),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, gameData.GameUid!, Color.White);

                var statsBackground = ImageExtensions.CreateRoundedRectanglePath(700, 250, 15).Translate(50, 170);
                ctx.Fill(OverlayColor, statsBackground);

                ctx.DrawText("Deepest Descent: ", m_NormalFont, Color.White, new PointF(80, 200));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 200),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, abyssData.MaxFloor!, Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(80, 250), new PointF(720, 250));

                ctx.DrawText("Battles Fought: ", m_NormalFont, Color.White, new PointF(80, 280));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 280),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{abyssData.TotalWinTimes}/{abyssData.TotalBattleTimes}", Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(80, 330), new PointF(720, 330));

                ctx.DrawText("Total Abyss Stars: ", m_NormalFont, Color.White, new PointF(80, 360));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 360),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{abyssData.TotalStar}", Color.White);

                var mostUsedBackground = ImageExtensions.CreateRoundedRectanglePath(700, 260, 15).Translate(50, 440);
                ctx.Fill(OverlayColor, mostUsedBackground);
                ctx.DrawText("Most Used Characters", m_NormalFont, Color.White, new PointF(80, 460));

                var revealRankImages = abyssData.RevealRank!
                    .ToDictionary(x => x.AvatarId!.Value,
                        x =>
                        {
                            var entry = portraitImages.FirstOrDefault(y => y.Key.Id == x.AvatarId);
                            return entry.Key.GetStyledAvatarImage(entry.Value, constMap[x.AvatarId!.Value],
                                x.Value.ToString()!);
                        });
                var revealRank = GetRosterImage(abyssData.RevealRank!.Select(x => x.AvatarId!.Value).ToList(),
                    revealRankImages);
                disposableResources.AddRange(revealRankImages.Values);
                disposableResources.Add(revealRank);
                ctx.DrawImage(revealRank, new Point(75, 500), 1f);

                var overlay = ImageExtensions.CreateRoundedRectanglePath(700, 150, 15).Translate(50, 720);

                for (int i = 0; i < 5; i++)
                {
                    ctx.Fill(OverlayColor, overlay);
                    overlay = overlay.Translate(0, 170);
                }

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 795),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Strongest Single Strike", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 795),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{abyssData.DamageRank!.First().Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.DamageRank!.First().AvatarId!.Value], new Point(50, 700), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 965),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Defeats", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 965),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{abyssData.DefeatRank!.First().Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.DefeatRank!.First().AvatarId!.Value], new Point(50, 870), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1135),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 1135),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{abyssData.TakeDamageRank!.First().Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.TakeDamageRank!.First().AvatarId!.Value], new Point(50, 1040),
                    1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1305),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Skills Cast", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 1305),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{abyssData.NormalSkillRank!.First().Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.NormalSkillRank!.First().AvatarId!.Value], new Point(50, 1210),
                    1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1475),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Bursts Unleashed", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(720, 1475),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    }, $"{abyssData.EnergySkillRank!.First().Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.EnergySkillRank!.First().AvatarId!.Value], new Point(50, 1380),
                    1f);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                    {
                        Origin = new Vector2(795, 80),
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, $"Floor {floor}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(1385, 52),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{floorData.Star}/{floorData.MaxStar}", Color.White);

                ctx.DrawImage(m_AbyssStarIconLit, new Point(1395, 47), 1f);
                for (int i = 0; i < floorData.Levels!.Count; i++)
                {
                    var level = floorData.Levels![i];
                    var offset = i * 490 + 160;
                    var rosterBackground = ImageExtensions.CreateRoundedRectanglePath(670, 470, 15)
                        .Translate(785, offset - 60);
                    ctx.Fill(OverlayColor, rosterBackground);
                    ctx.DrawText($"Chamber {level.Index}", m_NormalFont, Color.White,
                        new PointF(810, offset - 40));
                    for (int j = 0; j < 3; j++)
                    {
                        int xOffset = 1310 + j * 40;
                        ctx.DrawImage(i < floorData.Star ? m_AbyssStarIconLit : m_AbyssStarIconUnlit,
                            new Point(xOffset, offset - 45), 1f);
                    }

                    for (int j = 0; j < level.Battles!.Count; j++)
                    {
                        var battle = level.Battles![j];
                        var rosterImage =
                            GetRosterImage(battle.Avatars!.Select(x => x.Id!.Value).ToList(), imageDict);
                        disposableResources.Add(rosterImage);
                        int yOffset = offset + j * 200;
                        ctx.DrawImage(rosterImage, new Point(795, yOffset), 1f);
                    }
                }
            });

            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to get abyss card for {GameUid}", gameData.GameUid!);
            throw;
        }
        finally
        {
            foreach (var resource in disposableResources) resource.Dispose();
        }
    }

    private static Image<Rgba32> GetRosterImage(List<int> avatarIds, Dictionary<int, Image<Rgba32>> imageDict)
    {
        const int avatarWidth = 150;

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
}
