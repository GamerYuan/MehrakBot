using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Zzz.Tower;

public class ZzzTowerCardService : CardServiceBase<ZzzTowerData>
{
    private Image m_MedalIcon = null!;
    private Image m_MvpIcon = null!;

    private static readonly Color RankOverlayColor = Color.FromPixel(new Rgba32(255, 255, 255, 69));
    private static readonly Color LocalBackgroundColor = Color.FromPixel(new Rgba32(69, 69, 69, 255));
    private const int DisplayEntryHeight = 200;
    private const int DisplayEntryWidth = 500;

    private readonly RichTextOptions m_DisplayScoreOptions;
    private readonly RichTextOptions m_DisplayRankOptions;

    public ZzzTowerCardService(IImageRepository imageRepository,
        ILogger<ZzzTowerCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Simulated Battle Trial",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", 48f, 28f, null, null))
    {
        m_DisplayScoreOptions = new RichTextOptions(Fonts.Title)
        {
            Origin = new Vector2(210, 170),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        m_DisplayRankOptions = new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(280, 85),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        using (var medalStream = await ImageRepository.DownloadFileToStreamAsync(
            string.Format(FileNameFormat.Zzz.TowerMedal, "s3"), cancellationToken))
        {
            m_MedalIcon = await Image.LoadAsync(medalStream, cancellationToken);
        }

        using (var mvpStream = await ImageRepository.DownloadFileToStreamAsync(
            string.Format("zzz/tower_mvp.png"), cancellationToken))
        {
            m_MvpIcon = await Image.LoadAsync(mvpStream, cancellationToken);
        }
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(1, 1);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzTowerData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charMap = context.GetParameter<Dictionary<int, (int Level, int Rank)>>("charMap");

        if (charMap == null)
        {
            Logger.LogError("charMap cannot be null!");
            throw new ArgumentNullException(nameof(charMap));
        }

        var avatarImages = await context.Data.DisplayAvatarRankList
            .OrderByDescending(x => x.Score)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var image = await Image.LoadAsync<Rgba32>(stream, token);
                var (level, rarity) = charMap.GetValueOrDefault(x.AvatarId, (0, 0));
                var avatar = new ZzzAvatar(x.AvatarId, level, x.Rarity[0], rarity, image);
                disposables.Add(avatar);
                return (x, avatar);
            })
            .ToListAsync(cancellationToken);

        const int width = 2 * DisplayEntryWidth + 150;
        var height = 500 + (int)Math.Ceiling(context.Data.DisplayAvatarRankList.Count / 2f) * DisplayEntryHeight;

        background.Mutate(ctx => ctx.Resize(width, height));

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.Fill(Brushes.Solid(LocalBackgroundColor), new Rectangle(0, 0, background.Width, background.Height));
            });

            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 100),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Endless Tower: Glory", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(width - 50, 70),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname} · IK {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(width - 50, 100),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.GameUid}", Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(DisplayEntryWidth, DisplayEntryHeight, new PointF(50, 200),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                canvas.DrawImage(m_MedalIcon, m_MedalIcon.Bounds,
                    new RectangleF(60, 200 + (200 - m_MedalIcon.Height) / 2, m_MedalIcon.Width, m_MedalIcon.Height),
                    KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(240, 250),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Highest Clear", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(530, 250),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.Data.LayerInfo.ClimbingTowerLayer.ToString(), Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(240, 335),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Total Points", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(530, 335),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, FormatNumberWithSuffix(context.Data.LayerInfo.TotalScore), Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(DisplayEntryWidth, DisplayEntryHeight, new PointF(600, 200),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                canvas.DrawImage(m_MvpIcon, m_MvpIcon.Bounds,
                    new RectangleF(610, 200 + (200 - m_MvpIcon.Height) / 2, m_MvpIcon.Width, m_MvpIcon.Height),
                    KnownResamplers.Bicubic);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(790, 250),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Medals Obtained", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1080, 250),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.Data.MvpInfo.FloorMvpNum.ToString(), Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(790, 335),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Ranking", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1080, 335),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{(float)context.Data.MvpInfo.RankPercent / 100:N2}%", Brushes.Solid(Color.White), null);

                var yOffset = 420;
                const int xOffset = 100 + DisplayEntryWidth;

                for (var i = 0; i < avatarImages.Count; i++)
                {
                    var even = i % 2 == 0;
                    var (towerAvatar, avatar) = avatarImages[i];
                    DrawStyledDisplayEntry(canvas, new Point(even ? 50 : xOffset, yOffset),
                        towerAvatar, avatar);
                    if (!even) yOffset += DisplayEntryHeight + 20;
                }
            });
        });
    }

    private void DrawStyledDisplayEntry(DrawingCanvas canvas, Point position, ZzzTowerAvatar data, ZzzAvatar avatar)
    {
        using var region = canvas.CreateRegion(new Rectangle(position, new Size(DisplayEntryWidth, DisplayEntryHeight)));
        region.Save(ClipOptions, new RoundedRectanglePolygon(0, 0, DisplayEntryWidth, DisplayEntryHeight, 15));
        region.Fill(Brushes.Solid(OverlayColor));
        region.Restore();

        avatar.DrawStyledAvatarImage(region, new Point(20, 10), "");

        region.DrawText(m_DisplayScoreOptions, data.Score.ToString(), Brushes.Solid(Color.White), null);

        region.DrawRoundedRectangleOverlay(140, 50, new PointF(210, 50),
            new RoundedRectangleOverlayStyle(RankOverlayColor, CornerRadius: 25));
        region.DrawText(m_DisplayRankOptions, $"{(float)data.RankPercent / 100:N2}%", Brushes.Solid(Color.White), null);
    }

    private static string FormatNumberWithSuffix(double num)
    {
        if (num >= 1000000000)
            return (num / 1000000000D).ToString("0.##") + "B";
        if (num >= 1000000)
            return (num / 1000000D).ToString("0.##") + "M";
        if (num >= 1000)
            return (num / 1000D).ToString("0.##") + "K";
        return num.ToString();
    }
}
