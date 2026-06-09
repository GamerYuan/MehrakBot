#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Utility;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Genshin.Stygian;

public class GenshinStygianCardService : CardServiceBase<StygianData>
{
    private Image<Rgba32>[] m_DifficultyLogo = [];

    public GenshinStygianCardService(IImageRepository imageRepository, ILogger<GenshinStygianCardService> logger, IApplicationMetrics metrics)
        : base(
            "Genshin Stygian",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 40, normalSize: 28, smallSize: null))
    { }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_DifficultyLogo = await Enumerable.Range(0, 7).ToAsyncEnumerable().Select(async (x, ct) =>
                await Image.LoadAsync<Rgba32>(
                    await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.StygianMedalName, x), ct), ct))
            .ToArrayAsync(cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.StygianBackgroundName, cancellationToken),
            cancellationToken);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<StygianData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var stygianInfo = context.Data;

        var stygianData = stygianInfo.Single;
        var avatarImages = await stygianData.Challenge!
            .SelectMany(x => x.Teams).DistinctBy(x => x.AvatarId)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token);
                var image = await Image.LoadAsync(stream, token);
                var avatar = new GenshinAvatar(x.AvatarId, x.Level, x.Rarity, x.Rank, image);
                disposables.Add(avatar);
                return avatar;
            })
            .ToDictionaryAsync(x => x, x => x, GenshinAvatarIdComparer.Instance, cancellationToken: cancellationToken);
        var bestAvatarImages = await stygianData.Challenge!.SelectMany(x => x.BestAvatar)
            .DistinctBy(x => x.AvatarId)
            .ToAsyncEnumerable()
            .ToDictionaryAsync((x, token) => ValueTask.FromResult(x.AvatarId),
                async (x, token) =>
                {
                    var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token);
                    image.Mutate(i => i.Resize(100, 0, KnownResamplers.Bicubic));
                    return image;
                }, cancellationToken: cancellationToken);
        var monsterImages = await stygianData.Challenge!.Select(x => x.Monster)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (x, token) => ValueTask.FromResult(x.MonsterId),
                async (x, token) => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();

        var tzi = context.GetParameter<Server>("server").GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Stygian Onslaught", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(50, 130),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.StartTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.EndTime))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(940, 130),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{stygianData.StygianBestRecord!.Second}s", Brushes.Solid(Color.White), null);

                var medalImage =
                    m_DifficultyLogo[
                        GetMedalIndex(stygianData.StygianBestRecord.Difficulty, stygianData.StygianBestRecord.Second)];
                canvas.DrawImage(medalImage, medalImage.Bounds,
                    new RectangleF(960, 60, medalImage.Width, medalImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1650, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1650, 130),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{context.GameProfile.GameUid}", Brushes.Solid(Color.White), null);

                for (var i = 0; i < stygianData.Challenge!.Count; i++)
                {
                    var challenge = stygianData.Challenge[i];
                    var yOffset = 170 + i * 320;
                    var originalMonsterImage = monsterImages[challenge.Monster.MonsterId];
                    var processedMonsterImage = originalMonsterImage.Clone(ctx =>
                    {
                        ctx.Resize(0, 600, KnownResamplers.Bicubic);
                        ctx.ApplyGradientFade(0.65f);
                        ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(-100, -125)));
                    });
                    disposables.Add(processedMonsterImage);

                    DrawChallengeImage(canvas, new Point(50, yOffset), challenge,
                        challenge.Teams.Select(x => lookup[x.AvatarId]), processedMonsterImage);

                    for (var j = 0; j < challenge.BestAvatar.Count; j++)
                    {
                        canvas.DrawRoundedRectangleOverlay(580, 145, new PointF(1070, yOffset + j * 155),
                            new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                        var bestAvatar = challenge.BestAvatar[j];
                        var avatarImage = bestAvatarImages[bestAvatar.AvatarId];
                        canvas.DrawImage(avatarImage, avatarImage.Bounds,
                            new RectangleF(1070, yOffset + 5 + j * 155, avatarImage.Width, avatarImage.Height), KnownResamplers.Bicubic);
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(1180, yOffset + 70 + j * 155),
                            VerticalAlignment = VerticalAlignment.Center,
                            WrappingLength = 275
                        }, GetBestAvatarString(bestAvatar.Type), Brushes.Solid(Color.White), null);
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(1600, yOffset + 70 + j * 155),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, bestAvatar.Dps, Brushes.Solid(Color.White), null);
                    }
                }

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(background.Width - 20, background.Height - 20),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                }
                );
            });
        });
    }

    private void DrawChallengeImage(DrawingCanvas canvas, Point location, Challenge data, IEnumerable<GenshinAvatar> teamAvatars, Image monsterImage)
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(1000, 300)));
        _ = region.Save(ClipOptions, new RoundedRectanglePolygon(new RectangleF(Point.Empty, new Size(1000, 300)), 15));

        region.Fill(Brushes.Solid(OverlayColor));
        region.DrawImage(monsterImage, monsterImage.Bounds,
            new RectangleF(0, 0, monsterImage.Width, monsterImage.Height), KnownResamplers.Bicubic);

        region.Restore();

        RosterImageBuilder.Draw(
            teamAvatars,
            new RosterLayout(MaxSlots: 4),
            new Point(340, 100),
            (point, avatar) => avatar.DrawStyledAvatarImage(region, point));

        region.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new Vector2(970, 65),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right
        }, $"{data.Second}s", Brushes.Solid(Color.White), null);
    }

    private static int GetMedalIndex(int difficulty, int clearTime)
    {
        return difficulty switch
        {
            6 => clearTime <= 180 ? 6 : 5,
            _ => difficulty - 1
        };
    }

    private static string GetBestAvatarString(int type)
    {
        return type switch
        {
            1 => "Strongest Single Strike",
            2 => "Highest Total Damage Dealt",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
}
