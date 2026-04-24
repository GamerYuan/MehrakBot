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

namespace Mehrak.Application.Services.Zzz.Tower;

public class ZzzTowerCardService : CardServiceBase<ZzzTowerData>
{
    private Image m_MedalIcon = null!;
    private Image m_MvpIcon = null!;

    private static readonly Color RankOverlayColor = Color.FromRgba(255, 255, 255, 69);
    private static readonly Color LocalBackgroundColor = Color.FromRgba(69, 69, 69, 255);
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
            string.Format(FileNameFormat.Zzz.FileName, "tower_mvp"), cancellationToken))
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
            .Select(async (ZzzTowerAvatar x, CancellationToken token) =>
            {
                using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                using var avatar = new ZzzAvatar(x.AvatarId, charMap[x.AvatarId].Level, x.Rarity[0], charMap[x.AvatarId].Rank,
                    await Image.LoadAsync(stream, token));
                return GetStyledDisplayEntry(x, avatar);
            })
            .ToListAsync();
        disposables.AddRange(avatarImages);

        const int width = 2 * DisplayEntryWidth + 150;
        var height = 500 + (int)Math.Ceiling(context.Data.DisplayAvatarRankList.Count / 2f) * DisplayEntryHeight;

        background.Mutate(ctx => ctx.Resize(width, height));

        background.Mutate(ctx =>
        {
            ctx.Clear(LocalBackgroundColor);

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 100),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Endless Tower: Glory", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(width - 50, 70),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{context.GameProfile.Nickname} · IK {context.GameProfile.Level}", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(width - 50, 100),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{context.GameProfile.GameUid}", Color.White);

            ctx.DrawRoundedRectangleOverlay(DisplayEntryWidth, DisplayEntryHeight, new PointF(50, 200),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
            ctx.DrawImage(m_MedalIcon, new Point(60, 200 + (200 - m_MedalIcon.Height) / 2), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(240, 250),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 120
            }, "Highest Clear", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(530, 250),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            }, context.Data.LayerInfo.ClimbingTowerLayer.ToString(), Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(240, 335),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 120
            }, "Total Points", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(530, 335),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            }, FormatNumberWithSuffix(context.Data.LayerInfo.TotalScore), Color.White);

            ctx.DrawRoundedRectangleOverlay(DisplayEntryWidth, DisplayEntryHeight, new PointF(600, 200),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
            ctx.DrawImage(m_MvpIcon, new Point(610, 200 + (200 - m_MvpIcon.Height) / 2), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(790, 250),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 120
            }, "Medals Obtained", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1080, 250),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            }, context.Data.MvpInfo.FloorMvpNum.ToString(), Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(790, 335),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 120
            }, "Ranking", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1080, 335),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{(float)context.Data.MvpInfo.RankPercent / 100:N2}%", Color.White);

            var yOffset = 420;
            const int xOffset = 100 + DisplayEntryWidth;

            for (var i = 0; i < avatarImages.Count; i++)
            {
                var even = i % 2 == 0;
                ctx.DrawImage(avatarImages[i], new Point(even ? 50 : xOffset, yOffset), 1f);
                if (!even) yOffset += DisplayEntryHeight + 20;
            }
        });
    }

    private Image GetStyledDisplayEntry(ZzzTowerAvatar data, ZzzAvatar avatar)
    {
        Image background = new Image<Rgba32>(DisplayEntryWidth, DisplayEntryHeight);
        background.Mutate(ctx =>
        {
            ctx.Clear(OverlayColor);
            using (var styledImage = avatar.GetStyledAvatarImage(""))
                ctx.DrawImage(styledImage, new Point(20, 10), 1f);

            ctx.DrawText(m_DisplayScoreOptions, data.Score.ToString(), Color.White);

            ctx.DrawRoundedRectangleOverlay(140, 50, new PointF(210, 50),
                new RoundedRectangleOverlayStyle(RankOverlayColor, CornerRadius: 25));
            ctx.DrawText(m_DisplayRankOptions, $"{(float)data.RankPercent / 100:N2}%", Color.White);
            ctx.ApplyRoundedCorners(15);
        });

        return background;
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
