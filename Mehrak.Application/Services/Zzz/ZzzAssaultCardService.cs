using Mehrak.Application.Models;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Text.Json;

namespace Mehrak.Application.Services.Zzz;

internal class ZzzAssaultCardService : ICardService<ZzzAssaultData>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<ZzzAssaultCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private Image m_StarLitImage = null!;
    private Image m_StarLitSmall = null!;
    private Image m_StarUnlitSmall = null!;
    private Image m_BaseBuddyImage = null!;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };
    private static readonly Color OverlayColor = Color.FromRgb(69, 69, 69);
    private static readonly Color BackgroundColor = Color.FromRgb(30, 30, 30);

    public ZzzAssaultCardService(IImageRepository imageRepository, ILogger<ZzzAssaultCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_StarLitImage = await Image.LoadAsync(await
            m_ImageRepository.DownloadFileToStreamAsync("zzz_assault_star"), cancellationToken);
        m_StarLitSmall = m_StarLitImage.Clone(ctx => ctx.Resize(0, 35));
        m_StarUnlitSmall = m_StarLitImage.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.5f);
            ctx.Resize(0, 35);
        });

        m_BaseBuddyImage = await Image.LoadAsync(await
            m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base")), cancellationToken);
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<ZzzAssaultData> context)
    {
        var data = context.Data;
        List<IDisposable> disposables = [];
        try
        {
            Dictionary<ZzzAvatar, Image<Rgba32>> avatarImages = await data.List.SelectMany(x => x.AvatarList)
                .DistinctBy(x => x.Id)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    Image image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(
                            string.Format(FileNameFormat.Zzz.AvatarName, x.Id)));
                    ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                    return (Avatar: avatar, Image: avatar.GetStyledAvatarImage());
                })
                .ToDictionaryAsync(x => x.Avatar, x => x.Image, comparer: ZzzAvatarIdComparer.Instance);
            disposables.AddRange(avatarImages.Keys);
            disposables.AddRange(avatarImages.Values);

            Dictionary<int, Image> buddyImages = await data.List.Select(x => x.Buddy)
                .Where(x => x is not null)
                .DistinctBy(x => x!.Id)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                {
                    Image image = await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(
                            string.Format(FileNameFormat.Zzz.BuddyName, x!.Id)));
                    return (BuddyId: x!.Id, Image: image);
                })
                .ToDictionaryAsync(x => x.BuddyId, x => x.Image);
            disposables.AddRange(buddyImages.Values);

            Dictionary<string, Stream> bossImages = await data.List.SelectMany(x => x.Boss)
                .ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Name),
                    async x =>
                    {
                        Stream stream = await m_ImageRepository.DownloadFileToStreamAsync($"zzz_assault_boss_{x.Name}");
                        return stream;
                    });
            Dictionary<string, Stream> buffImages = await data.List.SelectMany(x => x.Buff)
                .ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Name),
                    async x =>
                    {
                        Stream stream = await m_ImageRepository.DownloadFileToStreamAsync($"zzz_assault_buff_{x.Name}");
                        return stream;
                    });
            disposables.AddRange(bossImages.Values);
            disposables.AddRange(buffImages.Values);

            Dictionary<ZzzAvatar, Image<Rgba32>>.AlternateLookup<int> lookup = avatarImages.GetAlternateLookup<int>();

            int height = (data.List.Count * 270) + 200;
            Image<Rgba32> background = new(1050, height);

            background.Mutate(ctx =>
            {
                ctx.Clear(BackgroundColor);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Deadly Assault", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{data.StartTime.Day}/{data.StartTime.Month}/{data.StartTime.Year} - " +
                    $"{data.EndTime.Day}/{data.EndTime.Month}/{data.EndTime.Year}",
                    Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1000, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1000, 110),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Color.White);

                string totalScoreText = $"Total Score: {data.TotalScore}";
                FontRectangle totalScoreBounds = TextMeasurer.MeasureBounds(totalScoreText, new TextOptions(m_TitleFont));

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 160),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, totalScoreText, Color.White);
                IPath rankOverlay = ImageUtility.CreateRoundedRectanglePath(90, 40, 15).Translate(60 + totalScoreBounds.Width, 110);
                ctx.Fill(OverlayColor, rankOverlay);
                ctx.DrawText(new RichTextOptions(m_SmallFont)
                {
                    Origin = new Vector2(105 + (int)totalScoreBounds.Width, 145),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                }, $"{(float)data.RankPercent / 100:N2}%", Color.White);

                ctx.DrawImage(m_StarLitImage, new Point(160 + (int)totalScoreBounds.Width, 100), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(210 + (int)totalScoreBounds.Width, 150),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"x{data.TotalStar}", Color.White);

                for (int i = 0; i < data.List.Count; i++)
                {
                    AssaultFloorDetail floor = data.List[i];
                    int yOffset = 180 + (i * 270);
                    using Image bossImage = Image.Load(bossImages[floor.Boss[0].Name]);
                    using Image buffImage = Image.Load(buffImages[floor.Buff[0].Name]);
                    using Image<Rgba32> floorImage = GetFloorImage(floor, lookup, bossImage,
                        buffImage,
                        floor.Buddy == null ? null : buddyImages[floor.Buddy.Id]);
                    ctx.DrawImage(floorImage, new Point(50, yOffset), 1f);
                }
            });

            MemoryStream stream = new();
            await background.SaveAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error generating Zzz Assault card for GameUid: {GameUid}, Data:\n{AssaultData}",
                context.GameProfile.GameUid, JsonSerializer.Serialize(data));
            throw new CommandException("An error occurred while generating the Assault card image. Please try again later.", e);
        }
        finally
        {
            foreach (IDisposable disposable in disposables)
            {
                disposable.Dispose();
            }
        }
    }

    private Image<Rgba32> GetFloorImage(AssaultFloorDetail floor,
        Dictionary<ZzzAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup,
        Image bossImage,
        Image buffImage,
        Image? buddyImage = null)
    {
        Image<Rgba32> image = new(950, 260);
        image.Mutate(ctx =>
        {
            ctx.Clear(OverlayColor);
            ctx.DrawText(new RichTextOptions(floor.Boss[0].Name.Length > 25 ? m_SmallFont : m_NormalFont)
            {
                Origin = new Vector2(200, 34),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 500
            }, floor.Boss[0].Name, Color.White);
            string scoreText = floor.Score.ToString();
            FontRectangle scoreBounds = TextMeasurer.MeasureBounds(scoreText, new TextOptions(m_NormalFont));
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(925, 20),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            }, floor.Score.ToString(), Color.White);
            for (int i = 2; i >= 0; i--)
            {
                Image starImage = i < floor.Star ? m_StarLitSmall : m_StarUnlitSmall;
                ctx.DrawImage(starImage, new Point(885 - (int)scoreBounds.Width - (i * 35), 10), 1f);
            }
            ctx.DrawImage(bossImage, new Point(25, 15), 1f);
            using Image<Rgba32> rosterImage = GetRosterImage([.. floor.AvatarList.Select(x => avatarLookup[x.Id])], buddyImage);
            ctx.DrawImage(rosterImage, new Point(190, 60), 1f);
            ctx.DrawImage(buffImage, new Point(850, 110), 1f);

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }

    private Image<Rgba32> GetRosterImage(List<Image<Rgba32>> avatarImages, Image? buddyImage = null)
    {
        const int avatarWidth = 150;

        int offset = ((3 - avatarImages.Count) * avatarWidth / 2) + 10;

        Image<Rgba32> rosterImage = new(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            int x = 0;

            for (int i = 0; i < avatarImages.Count; i++)
            {
                x = offset + (i * (avatarWidth + 10));
                ctx.DrawImage(avatarImages[i], new Point(x, 0), 1f);
            }

            using Image<Rgba32> buddyBorder = new(150, 180);
            buddyBorder.Mutate(x =>
            {
                IPath outerPath = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
                x.Clear(Color.FromRgb(24, 24, 24));
                x.Draw(Color.Black, 4f, outerPath);
                x.DrawImage(buddyImage ?? m_BaseBuddyImage, new Point(-45, 0), 1f);
                x.ApplyRoundedCorners(15);
            });
            x = offset + (avatarImages.Count * (avatarWidth + 10));
            ctx.DrawImage(buddyBorder, new Point(x, 0), 1f);
        });

        return rosterImage;
    }
}
