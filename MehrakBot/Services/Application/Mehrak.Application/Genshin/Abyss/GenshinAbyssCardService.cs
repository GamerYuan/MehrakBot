#region

using System.Numerics;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
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

namespace Mehrak.Application.Genshin.Abyss;

internal class GenshinAbyssCardService : CardServiceBase<GenshinAbyssInformation>
{
    private Image<Rgba32> m_StarLit = null!;
    private Image<Rgba32> m_StarUnlit = null!;

    public GenshinAbyssCardService(
        IImageRepository imageRepository,
        ILogger<GenshinAbyssCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Genshin Abyss",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/genshin.ttf", titleSize: 40, normalSize: 28, smallSize: null))
    { }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        m_StarLit = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.AbyssStarsName, cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx => ctx.Brightness(0.35f));

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Genshin.AbyssBackgroundName, cancellationToken),
            cancellationToken);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<GenshinAbyssInformation> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var abyssData = context.Data;
        var constMap = context.GetParameter<Dictionary<int, int>>("constMap")
            ?? throw new CommandException("constMap parameter is missing for Abyss card generation");

        var floor = context.GetParameter<int>("floor");
        var server = context.GetParameter<Server>("server");

        var floorData = abyssData.Floors!.First(x => x.Index == floor);

        var portraitAvatarItems = floorData.Levels!
            .SelectMany(y => y.Battles!)
            .SelectMany(x => x.Avatars!).DistinctBy(x => x.Id).ToList();

        var portraitAvatarTasks = portraitAvatarItems
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), cancellationToken);
                var image = await Image.LoadAsync(stream, cancellationToken);
                var avatar = new GenshinAvatar(x.Id, x.Level, x.Rarity, constMap[x.Id], image);
                disposables.Add(avatar);
                return avatar;
            }).ToList();

        await Task.WhenAll(portraitAvatarTasks);

        var portraitAvatars = portraitAvatarTasks
            .Select(t => t.Result)
            .ToDictionary(x => x, x => x, GenshinAvatarIdComparer.Instance);

        var lookup = portraitAvatars.GetAlternateLookup<int>();

        var sideAvatarItems = abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
            .Concat(abyssData.EnergySkillRank!)
            .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
            .ToList();

        var sideAvatarTasks = sideAvatarItems
            .Select(async x => (x.AvatarId, Image: await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, cancellationToken)))
            .ToList();

        await Task.WhenAll(sideAvatarTasks);

        var sideAvatarImages = sideAvatarTasks
            .Select(t => t.Result)
            .ToDictionary(x => x.AvatarId, x => x.Image);

        var revealRankItems = abyssData.RevealRank!.ToList();

        var revealRankTasks = revealRankItems
            .Select(async x =>
            {
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(x.ToAvatarImageName(), cancellationToken);
                var image = await Image.LoadAsync(stream, cancellationToken);
                var avatar = new GenshinAvatar(x.AvatarId, 0, x.Rarity, constMap[x.AvatarId], image);
                disposables.Add(avatar);
                return (RevealRankAvatar: x, GenshinAvatar: avatar);
            }).ToList();

        await Task.WhenAll(revealRankTasks);

        var revealRankAvatars = revealRankTasks
            .Select(t => t.Result)
            .ToDictionary(
                x => x.GenshinAvatar,
                x => (x.GenshinAvatar, Text: x.RevealRankAvatar.Value.ToString()!),
                GenshinAvatarIdComparer.Instance);

        var tzi = server.GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Spiral Abyss", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(750, 80),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.StartTime!))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.EndTime!))
                        .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                    Brushes.Solid(Color.White), null);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(50, 110)
                }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}",
                    Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(750, 110),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.GameProfile.GameUid!, Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(700, 250, new PointF(50, 170),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(80, 200)
                }, "Deepest Descent: ", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 200),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, abyssData.MaxFloor!, Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(80, 250), new PointF(720, 250)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(80, 280)
                }, "Battles Fought: ", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 280),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{abyssData.TotalWinTimes}/{abyssData.TotalBattleTimes}", Brushes.Solid(Color.White), null);
                canvas.Draw(Pens.Solid(Color.White, 2f),
                    new PathBuilder().AddLine(new PointF(80, 330), new PointF(720, 330)).Build());

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(80, 360)
                }, "Total Abyss Stars: ", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 360),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{abyssData.TotalStar}", Brushes.Solid(Color.White), null);

                canvas.DrawRoundedRectangleOverlay(700, 260, new PointF(50, 440),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(80, 460)
                }, "Most Used Characters", Brushes.Solid(Color.White), null);

                RosterImageBuilder.Draw(
                    abyssData.RevealRank!.Select(x => revealRankAvatars.GetAlternateLookup<int>()[x.AvatarId]),
                    new RosterLayout(MaxSlots: 4),
                    new Point(75, 500),
                    (point, item) => item.GenshinAvatar.DrawStyledAvatarImage(canvas, point, item.Text));

                for (var i = 0; i < 5; i++)
                {
                    canvas.DrawRoundedRectangleOverlay(700, 150, new PointF(50, 720 + i * 170),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                }

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(200, 795),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Strongest Single Strike", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 795),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.DamageRank![0].Value}", Brushes.Solid(Color.White), null);
                var damageRankImage = sideAvatarImages[abyssData.DamageRank![0].AvatarId];
                canvas.DrawImage(damageRankImage, damageRankImage.Bounds,
                    new RectangleF(50, 700, damageRankImage.Width, damageRankImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(200, 965),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Defeats", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 965),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.DefeatRank![0].Value}", Brushes.Solid(Color.White), null);
                var defeatRankImage = sideAvatarImages[abyssData.DefeatRank![0].AvatarId];
                canvas.DrawImage(defeatRankImage, defeatRankImage.Bounds,
                    new RectangleF(50, 870, defeatRankImage.Width, defeatRankImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(200, 1135),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 1135),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.TakeDamageRank![0].Value}", Brushes.Solid(Color.White), null);
                var takeDamageRankImage = sideAvatarImages[abyssData.TakeDamageRank![0].AvatarId];
                canvas.DrawImage(takeDamageRankImage, takeDamageRankImage.Bounds,
                    new RectangleF(50, 1040, takeDamageRankImage.Width, takeDamageRankImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(200, 1305),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Skills Cast", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 1305),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.NormalSkillRank![0].Value}", Brushes.Solid(Color.White), null);
                var normalSkillRankImage = sideAvatarImages[abyssData.NormalSkillRank![0].AvatarId];
                canvas.DrawImage(normalSkillRankImage, normalSkillRankImage.Bounds,
                    new RectangleF(50, 1210, normalSkillRankImage.Width, normalSkillRankImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(200, 1475),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Bursts Unleashed", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(720, 1475),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.EnergySkillRank![0].Value}", Brushes.Solid(Color.White), null);
                var energySkillRankImage = sideAvatarImages[abyssData.EnergySkillRank![0].AvatarId];
                canvas.DrawImage(energySkillRankImage, energySkillRankImage.Bounds,
                    new RectangleF(50, 1380, energySkillRankImage.Width, energySkillRankImage.Height), KnownResamplers.Bicubic);

                canvas.DrawText(new RichTextOptions(Fonts.Title)
                {
                    Origin = new Vector2(795, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"Floor {floor}", Brushes.Solid(Color.White), null);
                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(1385, 52),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{floorData.Star}/{floorData.MaxStar}", Brushes.Solid(Color.White), null);

                canvas.DrawImage(m_StarLit, m_StarLit.Bounds,
                    new RectangleF(1395, 47, m_StarLit.Width, m_StarLit.Height), KnownResamplers.Bicubic);
                for (var i = 0; i < 3; i++)
                {
                    var offset = i * 490 + 160;
                    canvas.DrawRoundedRectangleOverlay(670, 470, new PointF(785, offset - 60),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                    canvas.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new PointF(810, offset - 40)
                    }, $"Chamber {i + 1}", Brushes.Solid(Color.White), null);

                    if (i >= floorData.Levels!.Count)
                    {
                        canvas.DrawText(new RichTextOptions(Fonts.Normal)
                        {
                            Origin = new Vector2(1120, offset + 175),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, "No Clear Records", Brushes.Solid(Color.White), null);

                        for (var j = 0; j < 3; j++)
                        {
                            var xOffset = 1310 + j * 40;
                            canvas.DrawImage(m_StarUnlit, m_StarUnlit.Bounds,
                                new RectangleF(xOffset, offset - 45, m_StarUnlit.Width, m_StarUnlit.Height), KnownResamplers.Bicubic);
                        }

                        continue;
                    }

                    var level = floorData.Levels![i];
                    for (var j = 0; j < 3; j++)
                    {
                        var xOffset = 1310 + j * 40;
                        var starImage = j < floorData.Levels[i].Star ? m_StarLit : m_StarUnlit;
                        canvas.DrawImage(starImage, starImage.Bounds,
                            new RectangleF(xOffset, offset - 45, starImage.Width, starImage.Height), KnownResamplers.Bicubic);
                    }

                    for (var j = 0; j < level.Battles!.Count; j++)
                    {
                        var battle = level.Battles![j];
                        var yOffset = offset + j * 200;
                        RosterImageBuilder.Draw(
                            battle.Avatars!.Select(x => lookup[x.Id]),
                            new RosterLayout(MaxSlots: 4),
                            new Point(795, yOffset),
                            (point, avatar) => avatar.DrawStyledAvatarImage(canvas, point));
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
}
