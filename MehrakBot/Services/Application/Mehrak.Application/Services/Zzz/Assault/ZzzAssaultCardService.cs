#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Assault;

internal class ZzzAssaultCardService : CardServiceBase<ZzzAssaultData>
{
    private Image m_StarLitImage = null!;
    private Image m_StarLitSmall = null!;
    private Image m_StarUnlitSmall = null!;
    private Image m_BaseBuddyImage = null!;

    private static readonly Color BackgroundColor = Color.FromRgb(30, 30, 30);
    private static readonly Color LocalOverlayColor = Color.FromRgb(69, 69, 69);

    public ZzzAssaultCardService(IImageRepository imageRepository,
        ILogger<ZzzAssaultCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz DA",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", titleSize: 40, normalSize: 28, smallSize: 20))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_StarLitImage = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync("zzz_assault_star", cancellationToken),
            cancellationToken);
        m_StarLitSmall = m_StarLitImage.Clone(ctx => ctx.Resize(0, 35));
        m_StarUnlitSmall = m_StarLitImage.Clone(ctx =>
        {
            ctx.Grayscale();
            ctx.Brightness(0.5f);
            ctx.Resize(0, 35);
        });

        m_BaseBuddyImage = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.BuddyName, "base"), cancellationToken),
            cancellationToken);
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(1, 1);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzAssaultData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var data = context.Data;
        var height = data.List.Count * 270 + 200;

        var avatarImages = await data.List.SelectMany(x => x.AvatarList)
            .DistinctBy(x => x.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                        x.ToImageName(), token), token);
                ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                return (Avatar: avatar, Image: avatar.GetStyledAvatarImage());
            })
            .ToDictionaryAsync(x => x.Avatar, x => x.Image, ZzzAvatarIdComparer.Instance);
        disposables.AddRange(avatarImages.Keys);
        disposables.AddRange(avatarImages.Values);

        var buddyImages = await data.List.Select(x => x.Buddy)
            .Where(x => x is not null)
            .DistinctBy(x => x!.Id)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                        x!.ToImageName(), token), token);
                return (BuddyId: x!.Id, Image: image);
            })
            .ToDictionaryAsync(x => x.BuddyId, x => x.Image);
        disposables.AddRange(buddyImages.Values);

        var bossImages = await data.List.SelectMany(x => x.Boss)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.Name),
                async (x, token) => await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token));
        disposables.AddRange(bossImages.Values);

        var buffImages = await data.List.SelectMany(x => x.Buff)
            .DistinctBy(x => x.Name)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(async (x, token) => await Task.FromResult(x.Name),
                async (x, token) => await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token));
        disposables.AddRange(buffImages.Values);

        var lookup = avatarImages.GetAlternateLookup<int>();

        background.Mutate(ctx => ctx.Resize(1050, height));

        background.Mutate(ctx =>
        {
            ctx.Clear(BackgroundColor);

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Deadly Assault", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 110),
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{data.StartTime.Day}/{data.StartTime.Month}/{data.StartTime.Year} - " +
                $"{data.EndTime.Day}/{data.EndTime.Month}/{data.EndTime.Year}",
                Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1000, 80),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1000, 110),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            },
                $"{context.GameProfile.GameUid}", Color.White);

            var totalScoreText = $"Total Score: {data.TotalScore}";
            var totalScoreBounds =
                TextMeasurer.MeasureBounds(totalScoreText, new TextOptions(Fonts.Title));

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 160),
                VerticalAlignment = VerticalAlignment.Bottom
            }, totalScoreText, Color.White);
            ctx.DrawRoundedRectangleOverlay(90, 40, new PointF(60 + totalScoreBounds.Width, 110),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, CornerRadius: 15));
            ctx.DrawText(new RichTextOptions(Fonts.Small!)
            {
                Origin = new Vector2(105 + (int)totalScoreBounds.Width, 145),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            }, $"{(float)data.RankPercent / 100:N2}%", Color.White);

            ctx.DrawImage(m_StarLitImage, new Point(160 + (int)totalScoreBounds.Width, 100), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(210 + (int)totalScoreBounds.Width, 150),
                VerticalAlignment = VerticalAlignment.Bottom
            }, $"x{data.TotalStar}", Color.White);

            for (var i = 0; i < data.List.Count; i++)
            {
                var floor = data.List[i];
                var yOffset = 180 + i * 270;
                var floorImage = GetFloorImage(floor, lookup, bossImages[floor.Boss[0].Name],
                    buffImages[floor.Buff[0].Name],
                    floor.Buddy == null ? null : buddyImages[floor.Buddy.Id],
                    disposables);
                disposables.Add(floorImage);
                ctx.DrawImage(floorImage, new Point(50, yOffset), 1f);
            }
        });
    }

    private Image<Rgba32> GetFloorImage(AssaultFloorDetail floor,
        Dictionary<ZzzAvatar, Image<Rgba32>>.AlternateLookup<int> avatarLookup,
        Image bossImage,
        Image buffImage,
        Image? buddyImage,
        DisposableBag disposables)
    {
        Image<Rgba32> image = new(950, 260);
        image.Mutate(ctx =>
        {
            ctx.Clear(LocalOverlayColor);
            ctx.DrawText(new RichTextOptions(floor.Boss[0].Name.Length > 25 ? Fonts.Small! : Fonts.Normal)
            {
                Origin = new Vector2(200, 34),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 500
            }, floor.Boss[0].Name, Color.White);
            var scoreText = floor.Score.ToString();
            var scoreBounds = TextMeasurer.MeasureBounds(scoreText, new TextOptions(Fonts.Normal));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(925, 20),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right
            }, floor.Score.ToString(), Color.White);
            for (var i = 2; i >= 0; i--)
            {
                var starImage = i < floor.Star ? m_StarLitSmall : m_StarUnlitSmall;
                ctx.DrawImage(starImage, new Point(885 - (int)scoreBounds.Width - i * 35, 10), 1f);
            }

            ctx.DrawImage(bossImage, new Point(25, 15), 1f);
            var styledBuddy = GetStyledBuddyImage(buddyImage);
            disposables.Add(styledBuddy);
            using var rosterImage = RosterImageBuilder.Build(
                floor.AvatarList.Select(x => avatarLookup[x.Id]),
                new RosterLayout(MaxSlots: 3),
                styledBuddy);
            ctx.DrawImage(rosterImage, new Point(190, 60), 1f);
            ctx.DrawImage(buffImage, new Point(850, 110), 1f);

            ctx.ApplyRoundedCorners(15);
        });
        return image;
    }

    private Image<Rgba32> GetStyledBuddyImage(Image? buddyImage)
    {
        Image<Rgba32> buddyBorder = new(150, 180);
        buddyBorder.Mutate(x =>
        {
            var outerPath = ImageUtility.CreateRoundedRectanglePath(150, 180, 15);
            x.Clear(Color.FromRgb(24, 24, 24));
            x.Draw(Color.Black, 4f, outerPath);
            x.DrawImage(buddyImage ?? m_BaseBuddyImage, new Point(-45, 0), 1f);
            x.ApplyRoundedCorners(15);
        });
        return buddyBorder;
    }
}
