#region

using System.Numerics;
using System.Text.Json;
using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ImageExtensions = MehrakCore.Utility.ImageExtensions;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Stygian;

public class GenshinStygianCardService : ICommandService<GenshinStygianCommandExecutor>
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

    private readonly Image[] m_DifficultyLogo;
    private readonly Image m_Background;

    public GenshinStygianCardService(ImageRepository imageRepository, ILogger<GenshinStygianCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        var collection = new FontCollection();
        var fontFamily = collection.Add("Assets/Fonts/genshin.ttf");
        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);

        m_DifficultyLogo = Enumerable.Range(0, 7).Select(x =>
            Image.Load(m_ImageRepository.DownloadFileToStreamAsync($"genshin_stygian_medal_{x}").GetAwaiter()
                .GetResult())).ToArray();
        m_Background = Image.Load(m_ImageRepository.DownloadFileToStreamAsync("genshin_stygian_bg").GetAwaiter()
            .GetResult());
    }

    public async ValueTask<Stream> GetStygianCardImageAsync(StygianData stygianInfo, UserGameData gameData,
        Regions region, Dictionary<int, Stream> monsterImage)
    {
        var disposableResources = new List<IDisposable>();
        try
        {
            var stygianData = stygianInfo.Single;
            var avatarImages = await stygianData.Challenge!.SelectMany(x => x.Teams).DistinctBy(x => x.AvatarId)
                .ToAsyncEnumerable()
                .SelectAwait(async x =>
                    new GenshinAvatar(x.AvatarId, x.Level, x.Rarity, x.Rank,
                        await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync($"genshin_avatar_{x.AvatarId}"))))
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x),
                    async x => await Task.FromResult(x.GetStyledAvatarImage()), GenshinAvatarIdComparer.Instance);
            var bestAvatarImages = await stygianData.Challenge!.SelectMany(x => x.BestAvatar)
                .DistinctBy(x => x.AvatarId).ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.AvatarId),
                    async x => await Image.LoadAsync(
                        await m_ImageRepository.DownloadFileToStreamAsync($"genshin_side_avatar_{x.AvatarId}")));
            var monsterImages = await monsterImage.ToAsyncEnumerable()
                .ToDictionaryAwaitAsync(async x => await Task.FromResult(x.Key),
                    async x => await Image.LoadAsync(x.Value));
            disposableResources.AddRange(avatarImages.Keys);
            disposableResources.AddRange(avatarImages.Values);
            disposableResources.AddRange(bestAvatarImages.Values);
            disposableResources.AddRange(monsterImage.Values);
            disposableResources.AddRange(monsterImages.Values);

            var lookup = avatarImages.GetAlternateLookup<int>();

            var background = m_Background.CloneAs<Rgba32>();

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
                    var challenge = stygianData.Challenge[i];
                    var rosterImage = GetRosterImage(challenge.Teams.Select(x => x.AvatarId), lookup);
                    disposableResources.Add(rosterImage);
                    var monsterImageStream = monsterImages[challenge.Monster.MonsterId];
                    var challengeImage = GetChallengeImage(challenge, rosterImage, monsterImageStream);
                    disposableResources.Add(challengeImage);
                    var yOffset = 170 + i * 320;
                    ctx.DrawImage(challengeImage, new Point(50, yOffset), 1f);

                    for (int j = 0; j < challenge.BestAvatar.Count; j++)
                    {
                        var overlay = ImageExtensions.CreateRoundedRectanglePath(580, 145, 15)
                            .Translate(1070, yOffset + j * 155);
                        ctx.Fill(OverlayColor, overlay);
                        var bestAvatar = challenge.BestAvatar[j];
                        var avatarImage = bestAvatarImages[bestAvatar.AvatarId];
                        avatarImage.Mutate(x => x.Resize(100, 0, KnownResamplers.Bicubic));
                        ctx.DrawImage(avatarImage, new Point(1070, yOffset + 5 + j * 155), 1f);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(1180, yOffset + 70 + j * 155),
                            VerticalAlignment = VerticalAlignment.Center,
                            WrappingLength = 275
                        }, GetBestAvatarString(bestAvatar.Type), Color.White);
                        ctx.DrawText(new RichTextOptions(m_NormalFont)
                        {
                            Origin = new Vector2(1600, yOffset + 70 + j * 155),
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Right
                        }, bestAvatar.Dps, Color.White);
                    }
                }
            });

            var stream = new MemoryStream();
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

        var avatarIds = ids.ToList();
        int offset = (4 - avatarIds.Count) * avatarWidth / 2 + 10;

        var rosterImage = new Image<Rgba32>(650, 200);

        rosterImage.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);

            for (int i = 0; i < avatarIds.Count; i++)
            {
                int x = offset + i * (avatarWidth + 10);
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
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
