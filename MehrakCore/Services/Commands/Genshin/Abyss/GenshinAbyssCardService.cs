#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

    private static readonly Image<Rgba32> RosterBackground = new(670, 450);

    private readonly Image m_AbyssStarIconLit;
    private readonly Image m_AbyssStarIconUnlit;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    public GenshinAbyssCardService(ImageRepository imageRepository, ILogger<GenshinAbyssCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        RosterBackground.Mutate(x =>
        {
            x.Fill(Color.Black);
            x.ApplyRoundedCorners(30);
        });

        var collection = new FontCollection();
        var fontFamily = collection.Add("Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(16, FontStyle.Regular);

        m_AbyssStarIconLit = Image.LoadAsync(m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss_stars").Result)
            .Result;
        m_AbyssStarIconUnlit = m_AbyssStarIconLit.CloneAs<Rgba32>();
        m_AbyssStarIconUnlit.Mutate(ctx => ctx.Brightness(0.5f));
    }

    public async ValueTask<Stream> GetAbyssCardAsync(uint floor, string gameUid, GenshinAbyssInformation abyssData,
        Dictionary<int, int> constMap)
    {
        List<IDisposable> disposableResources = [];
        try
        {
            var imageDict = await abyssData.Floors!.SelectMany(x => x.Levels!.SelectMany(y => y.Battles!))
                .SelectMany(x => x.Avatars!).DistinctBy(x => x.Id).ToAsyncEnumerable().ToDictionaryAwaitAsync(
                    async x => await Task.FromResult(x.Id!.Value),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_avatar_{x.Id!.Value}")));
            disposableResources.AddRange(imageDict.Values);
            //var background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss"));
            var background = new Image<Rgba32>(1500, 1600);
            disposableResources.Add(background);
            var floorData = abyssData.Floors!.First(x => x.Index == floor);
            background.Mutate(ctx =>
            {
                ctx.Clear(Color.DarkSlateBlue);
                ctx.DrawText($"Floor {floor}", m_NormalFont, Color.White, new PointF(800, 40));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new PointF(1390, 40),
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, $"{floorData.Star}/{floorData.MaxStar}", Color.White);

                ctx.DrawImage(m_AbyssStarIconLit, new Point(1400, 35), 1f);
                for (int i = 0; i < floorData.Levels!.Count; i++)
                {
                    var level = floorData.Levels![i];
                    var offset = i * 500 + 140;
                    ctx.DrawText($"Chamber {level.Index}", m_NormalFont, Color.White,
                        new PointF(800, offset - 55));
                    ctx.DrawImage(RosterBackground, new Point(790, offset - 20), 0.3f);
                    for (int j = 0; j < level.Battles!.Count; j++)
                    {
                        var battle = level.Battles![j];
                        var rosterImage =
                            GetRosterImage(battle.Avatars!, imageDict, constMap);
                        disposableResources.Add(rosterImage);
                        int yOffset = offset + j * 230;
                        ctx.DrawImage(rosterImage, new Point(800, yOffset), 1f);
                    }

                    for (int j = 0; j < 3; j++)
                    {
                        int xOffset = 1075 + j * 40;
                        ctx.DrawImage(i < floorData.Star ? m_AbyssStarIconLit : m_AbyssStarIconUnlit,
                            new Point(xOffset, offset + 185), 1f);
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
            m_Logger.LogError(ex, "Failed to get abyss card for {GameUid}", gameUid);
            throw;
        }
        finally
        {
            foreach (var resource in disposableResources) resource.Dispose();
        }
    }

    private static Image<Rgba32> GetRosterImage(List<Avatar> avatarIds, Dictionary<int, Image> imageDict,
        Dictionary<int, int> constMap)
    {
        const int avatarWidth = 150;

        int offset = (4 - avatarIds.Count) * avatarWidth + 10;

        var rosterImage = new Image<Rgba32>(650, 200);
        var avatarImages = avatarIds.Select(x => x.GetStyledAvatarImage(imageDict[x.Id!.Value], constMap[x.Id!.Value]))
            .ToList();

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + i * (avatarWidth + 10);
                ctx.DrawImage(avatarImages[i], new Point(x, 0), 1f);
            }
        });

        return rosterImage;
    }
}
