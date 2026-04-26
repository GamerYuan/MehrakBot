#region

using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
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

namespace Mehrak.Application.Services.Genshin.Stygian;

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
                await Image.LoadAsync<Rgba32>(await ImageRepository.DownloadFileToStreamAsync($"genshin_stygian_medal_{x}"), ct))
            .ToArrayAsync(cancellationToken);

        StaticBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("genshin_stygian_bg", cancellationToken),
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
            .ToDictionaryAsync(x => x,
                x =>
                {
                    var styledImage = x.GetStyledAvatarImage();
                    disposables.Add(styledImage);
                    return styledImage;
                }, GenshinAvatarIdComparer.Instance, cancellationToken: cancellationToken);
        var bestAvatarImages = await stygianData.Challenge!.SelectMany(x => x.BestAvatar)
            .DistinctBy(x => x.AvatarId)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(async (x, token) => await ValueTask.FromResult(x.AvatarId),
                async (x, token) =>
                {
                    var image = await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token);
                    image.Mutate(i => i.Resize(100, 0, KnownResamplers.Bicubic));
                    return image;
                }, cancellationToken: cancellationToken);
        var monsterImages = await stygianData.Challenge!.Select(x => x.Monster)
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                async (x, token) => await ValueTask.FromResult(x.MonsterId),
                async (x, token) => await LoadImageFromRepositoryAsync(x.ToImageName(), disposables, token),
                cancellationToken: cancellationToken);

        var lookup = avatarImages.GetAlternateLookup<int>();

        var tzi = context.GetParameter<Server>("server").GetTimeZoneInfo();

        background.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, "Stygian Onslaught", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(50, 130),
                VerticalAlignment = VerticalAlignment.Bottom
            },
                $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.StartTime))
                    .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy} - " +
                $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.EndTime))
                    .ToOffset(tzi.BaseUtcOffset):dd/MM/yyyy}",
                Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(940, 130),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{stygianData.StygianBestRecord!.Second}s", Color.White);

            ctx.DrawImage(
                m_DifficultyLogo[
                    GetMedalIndex(stygianData.StygianBestRecord.Difficulty, stygianData.StygianBestRecord.Second)],
                new Point(960, 60), 1f);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1650, 80),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(1650, 130),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            },
                $"{context.GameProfile.GameUid}", Color.White);

            for (var i = 0; i < stygianData.Challenge!.Count; i++)
            {
                var challenge = stygianData.Challenge[i];
                var rosterImage = RosterImageBuilder.Build(
                    challenge.Teams.Select(x => lookup[x.AvatarId]),
                    new RosterLayout(MaxSlots: 4));
                disposables.Add(rosterImage);
                var monsterImageStream = monsterImages[challenge.Monster.MonsterId];
                var challengeImage = GetChallengeImage(challenge, rosterImage, monsterImageStream);
                disposables.Add(challengeImage);
                var yOffset = 170 + i * 320;
                ctx.DrawImage(challengeImage, new Point(50, yOffset), 1f);

                for (var j = 0; j < challenge.BestAvatar.Count; j++)
                {
                    ctx.DrawRoundedRectangleOverlay(580, 145, new PointF(1070, yOffset + j * 155),
                        new RoundedRectangleOverlayStyle(OverlayColor, CornerRadius: 15));
                    var bestAvatar = challenge.BestAvatar[j];
                    var avatarImage = bestAvatarImages[bestAvatar.AvatarId];
                    ctx.DrawImage(avatarImage, new Point(1070, yOffset + 5 + j * 155), 1f);
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(1180, yOffset + 70 + j * 155),
                        VerticalAlignment = VerticalAlignment.Center,
                        WrappingLength = 275
                    }, GetBestAvatarString(bestAvatar.Type), Color.White);
                    ctx.DrawText(new RichTextOptions(Fonts.Normal)
                    {
                        Origin = new Vector2(1600, yOffset + 70 + j * 155),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right
                    }, bestAvatar.Dps, Color.White);
                }
            }
        });
    }

    private Image<Rgba32> GetChallengeImage(Challenge data, Image rosterImage, Image monsterImage)
    {
        Image<Rgba32> challengeImage = new(1000, 300);
        challengeImage.Mutate(ctx =>
        {
            ctx.Fill(OverlayColor);
            monsterImage.Mutate(x =>
            {
                x.Resize(0, 600, KnownResamplers.Bicubic);
                x.ApplyGradientFade(0.65f);
            });
            ctx.DrawImage(monsterImage, new Point(-100, -125), 1f);
            ctx.DrawImage(rosterImage, new Point(340, 100), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(970, 65),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{data.Second}s", Color.White);
            ctx.ApplyRoundedCorners(15);
        });

        return challengeImage;
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
