#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Common;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Genshin.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Genshin.Abyss;

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
            await ImageRepository.DownloadFileToStreamAsync("genshin_abyss_stars", cancellationToken),
            cancellationToken);
        m_StarUnlit = m_StarLit.CloneAs<Rgba32>();
        m_StarUnlit.Mutate(ctx => ctx.Brightness(0.35f));

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("genshin_abyss_bg", cancellationToken),
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

        var portraitImages = await floorData.Levels!
            .SelectMany(y => y.Battles!)
            .SelectMany(x => x.Avatars!).DistinctBy(x => x.Id).ToAsyncEnumerable()
            .Select(async (x, token) =>
                new GenshinAvatar(x.Id, x.Level,
                    x.Rarity, constMap[x.Id], await Image.LoadAsync(
                        await ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token),
                    0))
            .ToDictionaryAsync(x => x,
                x => x.GetStyledAvatarImage(), GenshinAvatarIdComparer.Instance);

        var lookup = portraitImages.GetAlternateLookup<int>();

        var sideAvatarImages = await abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
            .Concat(abyssData.EnergySkillRank!)
            .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
            .ToAsyncEnumerable().ToDictionaryAsync(
                async (x, token) => await Task.FromResult(x.AvatarId),
                async (x, token) => await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(x.ToImageName()), token));

        var revealRankImages = await abyssData.RevealRank!
            .ToAsyncEnumerable()
            .Select(async (x, token) => (x, new GenshinAvatar(x.AvatarId, 0, x.Rarity,
                constMap[x.AvatarId],
                await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync(x.ToAvatarImageName()), token))))
            .ToDictionaryAsync(x => x.Item2,
                x => x.Item2.GetStyledAvatarImage(x.Item1.Value.ToString()!),
                GenshinAvatarIdComparer.Instance);

        disposables.AddRange(portraitImages.Keys);
        disposables.AddRange(portraitImages.Values);
        disposables.AddRange(revealRankImages.Keys);
        disposables.AddRange(revealRankImages.Values);
        disposables.AddRange(sideAvatarImages.Values);

        var tzi = server.GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Spiral Abyss", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(750, 80),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.StartTime!))
                    .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(abyssData.EndTime!))
                    .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                Color.White);

            ctx.DrawText($"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Fonts.Normal,
                Color.White,
                new PointF(50, 110));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(750, 110),
                HorizontalAlignment = HorizontalAlignment.Right
            }, context.GameProfile.GameUid!, Color.White);

            ctx.DrawRoundedRectangleOverlay(700, 250, new PointF(50, 170),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));

            ctx.DrawText("Deepest Descent: ", Fonts.Normal, Color.White, new PointF(80, 200));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 200),
                HorizontalAlignment = HorizontalAlignment.Right
            }, abyssData.MaxFloor!, Color.White);
            ctx.DrawLine(Color.White, 2f, new PointF(80, 250), new PointF(720, 250));

            ctx.DrawText("Battles Fought: ", Fonts.Normal, Color.White, new PointF(80, 280));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 280),
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{abyssData.TotalWinTimes}/{abyssData.TotalBattleTimes}", Color.White);
            ctx.DrawLine(Color.White, 2f, new PointF(80, 330), new PointF(720, 330));

            ctx.DrawText("Total Abyss Stars: ", Fonts.Normal, Color.White, new PointF(80, 360));
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 360),
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{abyssData.TotalStar}", Color.White);

            ctx.DrawRoundedRectangleOverlay(700, 260, new PointF(50, 440),
                new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
            ctx.DrawText("Most Used Characters", Fonts.Normal, Color.White, new PointF(80, 460));

            var revealRank = RosterImageBuilder.Build(
                abyssData.RevealRank!.Select(x => revealRankImages.GetAlternateLookup<int>()[x.AvatarId]),
                new RosterLayout(MaxSlots: 4));
            disposables.Add(revealRank);
            ctx.DrawImage(revealRank, new Point(75, 500), 1f);

            for (var i = 0; i < 5; i++)
            {
                ctx.DrawRoundedRectangleOverlay(700, 150, new PointF(50, 720 + i * 170),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
            }

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(200, 795),
                VerticalAlignment = VerticalAlignment.Center
            }, "Strongest Single Strike", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 795),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{abyssData.DamageRank![0].Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[abyssData.DamageRank![0].AvatarId], new Point(50, 700), 1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(200, 965),
                VerticalAlignment = VerticalAlignment.Center
            }, "Most Defeats", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 965),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{abyssData.DefeatRank![0].Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[abyssData.DefeatRank![0].AvatarId], new Point(50, 870), 1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(200, 1135),
                VerticalAlignment = VerticalAlignment.Center
            }, "Most Damage Taken", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 1135),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{abyssData.TakeDamageRank![0].Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[abyssData.TakeDamageRank![0].AvatarId], new Point(50, 1040),
                1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(200, 1305),
                VerticalAlignment = VerticalAlignment.Center
            }, "Elemental Skills Cast", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 1305),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{abyssData.NormalSkillRank![0].Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[abyssData.NormalSkillRank![0].AvatarId], new Point(50, 1210),
                1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(200, 1475),
                VerticalAlignment = VerticalAlignment.Center
            }, "Elemental Bursts Unleashed", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(720, 1475),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            }, $"{abyssData.EnergySkillRank![0].Value}", Color.White);
            ctx.DrawImage(sideAvatarImages[abyssData.EnergySkillRank![0].AvatarId], new Point(50, 1380),
                1f);

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(795, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, $"Floor {floor}", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1385, 52),
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{floorData.Star}/{floorData.MaxStar}", Color.White);

            ctx.DrawImage(m_StarLit, new Point(1395, 47), 1f);
            for (var i = 0; i < 3; i++)
            {
                var offset = i * 490 + 160;
                ctx.DrawRoundedRectangleOverlay(670, 470, new PointF(785, offset - 60),
                    new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                ctx.DrawText($"Chamber {i + 1}", Fonts.Normal, Color.White,
                    new PointF(810, offset - 40));

                if (i >= floorData.Levels!.Count)
                {
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(1120, offset + 175),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }, "No Clear Records", Color.White);

                    for (var j = 0; j < 3; j++)
                    {
                        var xOffset = 1310 + j * 40;
                        ctx.DrawImage(m_StarUnlit, new Point(xOffset, offset - 45), 1f);
                    }

                    continue;
                }

                var level = floorData.Levels![i];
                for (var j = 0; j < 3; j++)
                {
                    var xOffset = 1310 + j * 40;
                    ctx.DrawImage(j < floorData.Levels[i].Star ? m_StarLit : m_StarUnlit,
                        new Point(xOffset, offset - 45), 1f);
                }

                for (var j = 0; j < level.Battles!.Count; j++)
                {
                    var battle = level.Battles![j];
                    var rosterImage = RosterImageBuilder.Build(
                        battle.Avatars!.Select(x => lookup[x.Id]),
                        new RosterLayout(MaxSlots: 4));
                    disposables.Add(rosterImage);
                    var yOffset = offset + j * 200;
                    ctx.DrawImage(rosterImage, new Point(795, yOffset), 1f);
                }
            }
        });
    }
}
