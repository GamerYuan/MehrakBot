#region

using Mehrak.Application.Services.Genshin;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.Json;

#endregion

namespace Mehrak.Application.Services.Genshin;

public class GenshinStygianCardService : ICommandService<GenshinStygianCommandExecutor>, IAsyncInitializable
{
    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinStygianCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Image[] m_DifficultyLogo = [];
    private Image m_Background = null!;

    public GenshinStygianCardService(ImageRepository imageRepository, ILogger<GenshinStygianCardService> logger)
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
        m_DifficultyLogo = await Enumerable.Range(0, 7).ToAsyncEnumerable().SelectAwait(async x =>
            await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync($"genshin_stygian_medal_{x}")))
            .ToArrayAsync(cancellationToken: cancellationToken);
        m_Background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("genshin_stygian_bg"), cancellationToken);
    }

    public async ValueTask<Stream> GetStygianCardImageAsync(StygianData stygianInfo, UserGameData gameData,
        Regions region, Dictionary<int, Stream> monsterImage)
    {
        List<IDisposable> disposableResources = [];
        try
        {
            StygianChallengeData stygianData = stygianInfo.Single;
            Dictionary<GenshinAvatar, Image<Rgba32>> avatarImages = await stygianData.Challenge!.SelectMany(x => x.Teams).DistinctBy(x => x.AvatarId)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                    new GenshinAvatar(x.AvatarId, x.Level, x.Rarity, x.Rank,
                        await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.GenshinAvatarName, x.AvatarId)))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()), GenshinAvatarIdComparer.Instance);
            Dictionary<int, Image> bestAvatarImages = await stygianData.Challenge!.SelectMany(x => x.BestAvatar)
                .DistinctBy(x => x.AvatarId).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.AvatarId),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.GenshinSideAvatarName, x.AvatarId))));
            Dictionary<int, Image> monsterImages = await monsterImage.ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Key),
                    async x => await Image.LoadAsync(x.Value));
            disposableResources.AddRange(avatarImages.Keys);
            disposableResources.AddRange(avatarImages.Values);
            disposableResources.AddRange(bestAvatarImages.Values);
            disposableResources.AddRange(monsterImage.Values);
            disposableResources.AddRange(monsterImages.Values);

            Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> lookup = avatarImages.GetAlternateLookup<int>();

            Image<Rgba32> background = m_Background.CloneAs<Rgba32>();

            background.Mutate(ctx =>
            {
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, "Stygian Onslaught", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 130),
                    VerticalAlignment = VerticalAlignment.Bottom
                },
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.StartTime))
                        .ToOffset(region.GetTimeZoneInfo().BaseUtcOffset):dd/MM/yyyy} - " +
                    $"{DateTimeOffset.FromUnixTimeSeconds(long.Parse(stygianInfo.Schedule!.EndTime))
                        .ToOffset(region.GetTimeZoneInfo().BaseUtcOffset):dd/MM/yyyy}",
                    Color.White);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(940, 130),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{stygianData.StygianBestRecord!.Second}s", Color.White);

                ctx.DrawImage(
                    m_DifficultyLogo[
                        GetMedalIndex(stygianData.StygianBestRecord.Difficulty, stygianData.StygianBestRecord.Second)],
                    new Point(960, 60), 1f);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1650, 80),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{gameData.Nickname}·AR {gameData.Level}", Color.White);
                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(1650, 130),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Right
                },
                    $"{gameData.GameUid}", Color.White);

                for (int i = 0; i < stygianData.Challenge!.Count; i++)
                {
                    Challenge challenge = stygianData.Challenge[i];
                    Image<Rgba32> rosterImage = GetRosterImage(challenge.Teams.Select(x => x.AvatarId), lookup);
                    disposableResources.Add(rosterImage);
                    Image monsterImageStream = monsterImages[challenge.Monster.MonsterId];
                    Image<Rgba32> challengeImage = GetChallengeImage(challenge, rosterImage, monsterImageStream);
                    disposableResources.Add(challengeImage);
                    int yOffset = 170 + (i * 320);
                    ctx.DrawImage(challengeImage, new Point(50, yOffset), 1f);

                    for (int j = 0; j < challenge.BestAvatar.Count; j++)
                    {
                        IPath overlay = ImageUtility.CreateRoundedRectanglePath(580, 145, 15)
                            .Translate(1070, yOffset + (j * 155));
                        ctx.Fill(OverlayColor, overlay);
                        StygianBestAvatar bestAvatar = challenge.BestAvatar[j];
                        Image avatarImage = bestAvatarImages[bestAvatar.AvatarId];
                        avatarImage.Mutate(x => x.Resize(100, 0, KnownResamplers.Bicubic));
                        ctx.DrawImage(avatarImage, new Point(1070, yOffset + 5 + (j * 155)), 1f);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(1180, yOffset + 70 + (j * 155)),
                            VerticalAlignment = VerticalAlignment.Center,
                            WrappingLength = 275
                        }, GetBestAvatarString(bestAvatar.Type), Color.White);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(1600, yOffset + 70 + (j * 155)),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, bestAvatar.Dps, Color.White);
                    }
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to generate Stygian card image for uid {UserId}\n{Data}", gameData.GameUid,
                JsonSerializer.Serialize(stygianInfo));
            throw new CommandException("An error occurred while generating Stygian Onslaught card", ex);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
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
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(970, 65),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"{data.Second}s", Color.White);
            ctx.ApplyRoundedCorners(15);
        });

        return challengeImage;
    }

    private static Image<Rgba32> GetRosterImage(IEnumerable<int> ids,
        Dictionary<GenshinAvatar, Image<Rgba32>>.AlternateLookup<int> imageDict)
    {
        const int avatarWidth = 150;

        List<int> avatarIds = [.. ids];
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
