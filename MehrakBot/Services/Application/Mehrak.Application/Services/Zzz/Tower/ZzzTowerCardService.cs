using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Zzz.Tower;

public class ZzzTowerCardService : ICardService<ZzzTowerData>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApplicationMetrics m_Metrics;
    private readonly ILogger<ZzzTowerCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Image m_MedalIcon = null!;
    private Image m_MvpIcon = null!;
    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);
    private static readonly Color RankOverlayColor = Color.FromRgba(255, 255, 255, 69);
    private static readonly Color BackgroundColor = Color.FromRgba(69, 69, 69, 255);

    private const int DisplayEntryHeight = 200;
    private const int DisplayEntryWidth = 500;

    private readonly RichTextOptions m_DisplayScoreOptions;
    private readonly RichTextOptions m_DisplayRankOptions;

    public ZzzTowerCardService(IImageRepository imageRepository, IApplicationMetrics metrics, ILogger<ZzzTowerCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Metrics = metrics;
        m_Logger = logger;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(48, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_DisplayScoreOptions = new RichTextOptions(m_TitleFont)
        {
            Origin = new Vector2(210, 180),
            VerticalAlignment = VerticalAlignment.Bottom
        };

        m_DisplayRankOptions = new RichTextOptions(m_NormalFont)
        {
            Origin = new Vector2(280, 95),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_MedalIcon = await Image.LoadAsync(await
            m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.TowerMedal, "s3"),
                cancellationToken), cancellationToken);
        m_MvpIcon = await Image.LoadAsync(await
            m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Zzz.FileName, "tower_mvp"),
                cancellationToken), cancellationToken);

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzTowerCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<ZzzTowerData> context)
    {
        using var cardGenTimer = m_Metrics.ObserveCardGenerationDuration("zzz tower");
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Simulated Battle Trial", context.UserId);

        List<IDisposable> disposables = [];

        var charMap = context.GetParameter<Dictionary<int, (int Level, int Rank)>>("charMap");

        if (charMap == null)
        {
            m_Logger.LogError("charMap cannot be null!");
            throw new ArgumentNullException(nameof(charMap));
        }

        try
        {

            var avatarImages = await context.Data.DisplayAvatarRankList
                .OrderByDescending(x => x.Score)
                .ToAsyncEnumerable()
                .Select(async (ZzzTowerAvatar x, CancellationToken token) =>
                {
                    using var avatar = new ZzzAvatar(x.AvatarId, charMap[x.AvatarId].Level, x.Rarity[0], charMap[x.AvatarId].Rank,
                        await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token));
                    return GetStyledDisplayEntry(x, avatar);
                })
                .ToListAsync();
            disposables.AddRange(avatarImages);

            const int width = 2 * DisplayEntryWidth + 150;
            var height = 500 + (int)Math.Ceiling(context.Data.DisplayAvatarRankList.Count / 2f) * DisplayEntryHeight;
            using var background = new Image<Rgba32>(width, height);

            background.Mutate(ctx =>
            {
                ctx.Clear(BackgroundColor);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Endless Tower: Glory", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(width - 50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(width - 50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.GameUid}", Color.White);

                var statOverlay = ImageUtility.CreateRoundedRectanglePath(DisplayEntryWidth, DisplayEntryHeight, 15)
                    .Translate(50, 200);
                ctx.Fill(OverlayColor, statOverlay);
                ctx.DrawImage(m_MedalIcon, new Point(60, 200 + (200 - m_MedalIcon.Height) / 2), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(240, 260),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Highest Clear", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(530, 260),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.Data.LayerInfo.ClimbingTowerLayer.ToString(), Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(240, 345),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Total Points", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(530, 345),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, FormatNumberWithSuffix(context.Data.LayerInfo.TotalScore), Color.White);

                ctx.Fill(OverlayColor, statOverlay.Translate(550, 0));
                ctx.DrawImage(m_MvpIcon, new Point(610, 200 + (200 - m_MvpIcon.Height) / 2), 1f);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(790, 260),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Medals Obtained", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1080, 260),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.Data.MvpInfo.FloorMvpNum.ToString(), Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(790, 345),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 120
                }, "Ranking", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1080, 345),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{(float)context.Data.MvpInfo.RankPercent / 100:N2}%", Color.White);

                var yOffset = 420;
                const int xOffset = 100 + DisplayEntryWidth;

                for (var i = 0; i < avatarImages.Count; i++)
                {
                    var odd = i % 2 == 0;
                    ctx.DrawImage(avatarImages[i], new Point(odd ? 50 : xOffset, yOffset), 1f);
                    if (!odd) yOffset += DisplayEntryHeight + 20;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Simulated Battle Trial", context.UserId);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Simulated Battle Trial", context.UserId, JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate Simulated Battle Trial card", e);
        }
        finally
        {
            disposables.ForEach(x => x.Dispose());
        }
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

            var rankOverlay = ImageUtility.CreateRoundedRectanglePath(140, 50, 25)
                .Translate(210, 50);
            ctx.Fill(RankOverlayColor, rankOverlay);
            ctx.DrawText(m_DisplayRankOptions, $"{(float)data.RankPercent / 100:N2}%", Color.White);
            ctx.ApplyRoundedCorners(15);
        });

        return background;
    }

    private static string FormatNumberWithSuffix(double num)
    {
        if (num >= 1000000000)
        {
            return (num / 1000000000D).ToString("0.##") + "B";
        }
        if (num >= 1000000)
        {
            return (num / 1000000D).ToString("0.##") + "M";
        }
        if (num >= 1000)
        {
            return (num / 1000D).ToString("0.##") + "K";
        }
        return num.ToString();
    }
}
