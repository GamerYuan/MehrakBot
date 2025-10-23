#region

using Mehrak.Application.Models;
using Mehrak.Application.Services.Genshin.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

#endregion

namespace Mehrak.Application.Services.Genshin.Abyss;

internal class GenshinAbyssCardService :
    ICardService<GenshinEndGameGenerationContext<GenshinAbyssInformation>, GenshinAbyssInformation>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<GenshinAbyssCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private Image m_AbyssStarIconLit = null!;
    private Image m_AbyssStarIconUnlit = null!;
    private Image m_BackgroundImage = null!;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public GenshinAbyssCardService(IImageRepository imageRepository, ILogger<GenshinAbyssCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/genshin.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_AbyssStarIconLit = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss_stars"), cancellationToken);
        m_AbyssStarIconUnlit = m_AbyssStarIconLit.CloneAs<Rgba32>();
        m_AbyssStarIconUnlit.Mutate(ctx => ctx.Brightness(0.35f));

        m_BackgroundImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_abyss_bg"), cancellationToken);
    }

    public async Task<Stream> GetCardAsync(GenshinEndGameGenerationContext<GenshinAbyssInformation> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Abyss", context.UserId);
        Stopwatch stopwatch = Stopwatch.StartNew();

        List<IDisposable> disposableResources = [];
        var abyssData = context.Data;
        var floor = context.Floor;
        try
        {
            Floor floorData = abyssData.Floors!.First(x => x.Index == floor);

            Dictionary<GenshinAvatar, Image<Rgba32>> portraitImages = await floorData.Levels!.SelectMany(y => y.Battles!)
                .SelectMany(x => x.Avatars!).DistinctBy(x => x.Id).ToAsyncEnumerable()
                .SelectAwait(async x =>
                    new GenshinAvatar(x.Id, x.Level,
                        x.Rarity, context.ConstMap[x.Id], await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.AvatarName, x.Id))),
                        0))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()), GenshinAvatarIdComparer.Instance);

            Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> lookup = portraitImages.GetAlternateLookup<int>();

            Dictionary<int, Image> sideAvatarImages = await abyssData.DamageRank!.Concat(abyssData.DefeatRank!)
                .Concat(abyssData.EnergySkillRank!)
                .Concat(abyssData.NormalSkillRank!).Concat(abyssData.TakeDamageRank!).DistinctBy(x => x.AvatarId)
                .ToAsyncEnumerable().ToDictionaryAwaitAsync(
                    async x => await Task.FromResult(x.AvatarId),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.SideAvatarName, x.AvatarId))));
            Dictionary<GenshinAvatar, Image<Rgba32>> revealRankImages = await abyssData.RevealRank!
                .ToAsyncEnumerable()
                .SelectAwait(async x => (x, new GenshinAvatar(x.AvatarId, 0, x.Rarity,
                    context.ConstMap[x.AvatarId],
                    await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Genshin.AvatarName, x.AvatarId))))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Item2),
                    async x => await Task.FromResult(x.Item2.GetStyledAvatarImage(x.Item1.Value.ToString()!)),
                    GenshinAvatarIdComparer.Instance);
            disposableResources.AddRange(portraitImages.Keys);
            disposableResources.AddRange(portraitImages.Values);
            disposableResources.AddRange(revealRankImages.Keys);
            disposableResources.AddRange(revealRankImages.Values);
            disposableResources.AddRange(sideAvatarImages.Values);

            Image<Rgba32> background = m_BackgroundImage.CloneAs<Rgba32>();
            disposableResources.Add(background);

            var tzi = context.Server.GetTimeZoneInfo();

            background.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Spiral Abyss", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
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

                ctx.DrawText($"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", m_NormalFont, Color.White,
                    new PointF(50, 110));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(750, 110),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, context.GameProfile.GameUid!, Color.White);

                IPath statsBackground = ImageUtility.CreateRoundedRectanglePath(700, 250, 15).Translate(50, 170);
                ctx.Fill(OverlayColor, statsBackground);

                ctx.DrawText("Deepest Descent: ", m_NormalFont, Color.White, new PointF(80, 200));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 200),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, abyssData.MaxFloor!, Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(80, 250), new PointF(720, 250));

                ctx.DrawText("Battles Fought: ", m_NormalFont, Color.White, new PointF(80, 280));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 280),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{abyssData.TotalWinTimes}/{abyssData.TotalBattleTimes}", Color.White);
                ctx.DrawLine(Color.White, 2f, new PointF(80, 330), new PointF(720, 330));

                ctx.DrawText("Total Abyss Stars: ", m_NormalFont, Color.White, new PointF(80, 360));
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 360),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{abyssData.TotalStar}", Color.White);

                IPath mostUsedBackground = ImageUtility.CreateRoundedRectanglePath(700, 260, 15).Translate(50, 440);
                ctx.Fill(OverlayColor, mostUsedBackground);
                ctx.DrawText("Most Used Characters", m_NormalFont, Color.White, new PointF(80, 460));

                Image<Rgba32> revealRank = GetRosterImage([.. abyssData.RevealRank!.Select(x => x.AvatarId)],
                    revealRankImages.GetAlternateLookup<int>());
                disposableResources.AddRange(revealRankImages.Values);
                disposableResources.Add(revealRank);
                ctx.DrawImage(revealRank, new Point(75, 500), 1f);

                IPath overlay = ImageUtility.CreateRoundedRectanglePath(700, 150, 15).Translate(50, 720);

                for (int i = 0; i < 5; i++)
                {
                    ctx.Fill(OverlayColor, overlay);
                    overlay = overlay.Translate(0, 170);
                }

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 795),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Strongest Single Strike", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 795),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.DamageRank![0].Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.DamageRank![0].AvatarId], new Point(50, 700), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 965),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Defeats", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 965),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.DefeatRank![0].Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.DefeatRank![0].AvatarId], new Point(50, 870), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1135),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Most Damage Taken", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 1135),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.TakeDamageRank![0].Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.TakeDamageRank![0].AvatarId], new Point(50, 1040),
                    1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1305),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Skills Cast", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 1305),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.NormalSkillRank![0].Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.NormalSkillRank![0].AvatarId], new Point(50, 1210),
                    1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(200, 1475),
                    VerticalAlignment = VerticalAlignment.Center
                }, "Elemental Bursts Unleashed", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(720, 1475),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, $"{abyssData.EnergySkillRank![0].Value}", Color.White);
                ctx.DrawImage(sideAvatarImages[abyssData.EnergySkillRank![0].AvatarId], new Point(50, 1380),
                    1f);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(795, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"Floor {floor}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1385, 52),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{floorData.Star}/{floorData.MaxStar}", Color.White);

                ctx.DrawImage(m_AbyssStarIconLit, new Point(1395, 47), 1f);
                for (int i = 0; i < 3; i++)
                {
                    int offset = (i * 490) + 160;
                    IPath rosterBackground = ImageUtility.CreateRoundedRectanglePath(670, 470, 15)
                        .Translate(785, offset - 60);
                    ctx.Fill(OverlayColor, rosterBackground);
                    ctx.DrawText($"Chamber {i + 1}", m_NormalFont, Color.White,
                        new PointF(810, offset - 40));

                    if (i >= floorData.Levels!.Count)
                    {
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(1120, offset + 175),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }, "No Clear Records", Color.White);

                        for (int j = 0; j < 3; j++)
                        {
                            int xOffset = 1310 + (j * 40);
                            ctx.DrawImage(m_AbyssStarIconUnlit, new Point(xOffset, offset - 45), 1f);
                        }

                        continue;
                    }

                    Level level = floorData.Levels![i];
                    for (int j = 0; j < 3; j++)
                    {
                        int xOffset = 1310 + (j * 40);
                        ctx.DrawImage(j < floorData.Levels[i].Star ? m_AbyssStarIconLit : m_AbyssStarIconUnlit,
                            new Point(xOffset, offset - 45), 1f);
                    }

                    for (int j = 0; j < level.Battles!.Count; j++)
                    {
                        Battle battle = level.Battles![j];
                        Image<Rgba32> rosterImage =
                            GetRosterImage([.. battle.Avatars!.Select(x => x.Id)], lookup);
                        disposableResources.Add(rosterImage);
                        int yOffset = offset + (j * 200);
                        ctx.DrawImage(rosterImage, new Point(795, yOffset), 1f);
                    }
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Abyss", context.UserId,
                stopwatch.ElapsedMilliseconds);
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Abyss", context.UserId, JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate Abyss card", e);
        }
        finally
        {
            foreach (IDisposable resource in disposableResources) resource.Dispose();
        }
    }

    private static Image<Rgba32> GetRosterImage(List<int> avatarIds,
        Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> imageDict)
    {
        const int avatarWidth = 150;

        int offset = ((4 - avatarIds.Count) * avatarWidth / 2) + 10;

        Image<Rgba32> rosterImage = new(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + (i * (avatarWidth + 10));
                ctx.DrawImage(imageDict[avatarIds[i]], new Point(x, 0), 1f);
            }
        });

        return rosterImage;
    }
}
