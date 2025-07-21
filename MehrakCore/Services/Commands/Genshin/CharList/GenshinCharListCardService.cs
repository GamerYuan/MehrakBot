#region

using System.Numerics;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Commands.Genshin.CharList;

public class GenshinCharListCardService : ICommandService<GenshinCharListCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharListCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);
    private static readonly Color BlueBackgroundColor = Color.FromRgb(86, 130, 166);
    private static readonly Color GreenBackgroundColor = Color.FromRgb(79, 135, 111);
    private static readonly Color WhiteBackgroundColor = Color.FromRgb(128, 128, 130);

    private static readonly Color[] RarityColors =
    [
        WhiteBackgroundColor,
        GreenBackgroundColor,
        BlueBackgroundColor,
        PurpleBackgroundColor,
        GoldBackgroundColor
    ];

    private static readonly Color NormalConstColor = Color.FromRgba(69, 69, 69, 200);
    private static readonly Color GoldConstTextColor = Color.FromRgb(138, 101, 0);

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);
    private static readonly Color DarkOverlayColor = Color.FromRgba(0, 0, 0, 200);

    public GenshinCharListCardService(ImageRepository imageRepository, ILogger<GenshinCharListCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async ValueTask<Stream> GetCharListCardAsync(UserGameData gameData,
        List<GenshinBasicCharacterData> charData)
    {
        List<IDisposable> disposables = [];
        try
        {
            m_Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
                gameData.GameUid, charData.Count);

            var weaponImages = await charData.Select(x => x.Weapon).DistinctBy(x => x.Id).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Id!.Value),
                    async x =>
                    {
                        var image = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync($"genshin_{x.Id}"));
                        image.Mutate(ctx => ctx.Resize(150, 0, KnownResamplers.Bicubic));
                        return image;
                    });
            disposables.AddRange(weaponImages.Values);

            var avatarImages = await charData.OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Rarity)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    using var avatarImage = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_avatar_{x.Id}"));
                    return GetStyledCharacterImage(x, avatarImage, weaponImages[x.Weapon.Id!.Value]);
                })
                .ToListAsync();

            disposables.AddRange(avatarImages);

            var layout = ImageUtility.CalculateGridLayout(avatarImages.Count, 300, 180, [120, 50, 50, 50]);

            var background = new Image<Rgba32>(layout.OutputWidth, layout.OutputHeight);

            background.Mutate(ctx =>
            {
                ctx.Fill(Color.RebeccaPurple);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                    {
                        Origin = new Vector2(50, 80),
                        VerticalAlignment = VerticalAlignment.Bottom
                    }, $"{gameData.Nickname}·AR {gameData.Level}", Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, gameData.GameUid!, Color.White);

                foreach (var position in layout.ImagePositions)
                {
                    var image = avatarImages[position.ImageIndex];
                    ctx.DrawImage(image, new Point(position.X, position.Y), 1f);
                }
            });

            m_Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
                gameData.GameUid, charData.Count);
            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to get character list card for uid {UserId}\n{CharData}", gameData.GameUid,
                JsonSerializer.Serialize(charData));
            throw;
        }
        finally
        {
            foreach (var disposable in disposables) disposable.Dispose();
        }
    }

    private Image<Rgba32> GetStyledCharacterImage(GenshinBasicCharacterData charData, Image avatarImage,
        Image weaponImage)
    {
        var background = new Image<Rgba32>(300, 180);
        background.Mutate(ctx =>
        {
            ctx.Fill(RarityColors[charData.Rarity!.Value - 1], new RectangleF(0, 0, 150, 180));
            ctx.Fill(RarityColors[charData.Weapon.Rarity!.Value - 1], new RectangleF(150, 0, 150, 180));

            ctx.DrawImage(avatarImage, new Point(0, 0), 1f);
            ctx.DrawImage(weaponImage, new Point(150, 0), 1f);

            var charLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Level}", new TextOptions(m_SmallFont));
            var charLevel =
                ImageUtility.CreateRoundedRectanglePath((int)charLevelRect.Width + 40, (int)charLevelRect.Height + 20,
                    10);
            ctx.Fill(DarkOverlayColor, charLevel.Translate(-25, 110));
            ctx.DrawText(new RichTextOptions(m_SmallFont)
                {
                    Origin = new Vector2(5, 120 + charLevelRect.Height / 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"Lv. {charData.Level}", Color.White);

            var constIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(115, 115);
            switch (charData.ActivedConstellationNum)
            {
                case 6:
                    ctx.Fill(Color.Gold, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(130, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "6", GoldConstTextColor);
                    break;
                case > 0:
                    ctx.Fill(NormalConstColor, constIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(130, 130),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, $"{charData.ActivedConstellationNum}", Color.White);
                    break;
            }

            var weapLevelRect = TextMeasurer.MeasureSize($"Lv. {charData.Weapon.Level}", new TextOptions(m_SmallFont));
            var weapLevel =
                ImageUtility.CreateRoundedRectanglePath((int)weapLevelRect.Width + 40, (int)weapLevelRect.Height + 20,
                    10);
            ctx.Fill(DarkOverlayColor, weapLevel.Translate(285 - weapLevelRect.Width, 110));
            ctx.DrawText(new RichTextOptions(m_SmallFont)
                {
                    Origin = new PointF(295 - weapLevelRect.Width / 2, 120 + weapLevelRect.Height / 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"Lv. {charData.Weapon.Level}", Color.White);

            var refineIcon = ImageUtility.CreateRoundedRectanglePath(30, 30, 5).Translate(155, 115);
            switch (charData.Weapon.AffixLevel)
            {
                case 5:
                    ctx.Fill(Color.Gold, refineIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                    {
                        Origin = new Vector2(170, 130),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "5", GoldConstTextColor);
                    break;
                case > 0:
                    ctx.Fill(NormalConstColor, refineIcon);
                    ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(170, 130),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, $"{charData.Weapon.AffixLevel}", Color.White);
                    break;
            }

            ctx.DrawLine(OverlayColor, 2f, new PointF(150, -5), new PointF(150, 185));
            ctx.BoxBlur(2, new Rectangle(147, 0, 5, 180));

            ctx.Fill(Color.PeachPuff, new RectangleF(0, 150, 300, 30));
            ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(150, 165),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{charData.Name}", Color.Black);

            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }
}
