using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using System.Text.Json;

namespace MehrakCore.Services.Commands.Zzz.Defense;

internal class ZzzDefenseCardService : ICommandService<ZzzDefenseCommandExecutor>, IAsyncInitializable
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<ZzzDefenseCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    private Dictionary<char, Image> m_RatingImages;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Quality = 90,
        Interleaved = false
    };
    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    public ZzzDefenseCardService(ImageRepository imageRepository, ILogger<ZzzDefenseCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontCollection collection = new();
        FontFamily fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        char[] rating = ['S', 'A', 'B'];
        m_RatingImages = await rating.ToAsyncEnumerable()
            .SelectAwait(async x => (Rating: x, Image: await Image.LoadAsync(
                await m_ImageRepository.DownloadFileToStreamAsync($"zzz_rating_{x}"))))
            .ToDictionaryAsync(x => x.Rating, x => x.Image, cancellationToken: cancellationToken);
    }

    public async ValueTask<Stream> GetDefenseCardAsync(ZzzDefenseData data, UserGameData gameData)
    {
        try
        {
            IEnumerable<Task<(int Id, Image Image)>> avatarImageTask = data.AllFloorDetail
                .SelectMany(x => x.Node1.Avatars.Concat(x.Node2.Avatars))
                .DistinctBy(x => x!.Id)
                .Select(async avatar => (avatar.Id, Image: await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.ZzzAvatarName, avatar.Id)))));

            List<(int FloorNumber, FloorDetail? Data)> floorDetails = [.. Enumerable.Range(0, 4)
                        .Select(floorIndex =>
                        {
                            FloorDetail? floorData = data.AllFloorDetail
                                .FirstOrDefault(x => x.LayerIndex == floorIndex);
                            return (FloorNumber: floorIndex, Data: floorData);
                        })];

            int height = 180 + floorDetails.Chunk(2)
                .Select(x => x.All(y => y.Data == null || IsSmallBlob(y.Data)) ? 200 : 620).Sum();

            await Task.WhenAll(avatarImageTask);

            MemoryStream stream = new();
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error generating Zzz Defense card for UID {GameUid}, Data:\n{Data}",
                gameData.GameUid, JsonSerializer.Serialize(data));
            throw new CommandException("An error occurred while generating Shiyu Defense summary card", e);
        }
    }

    private static bool IsSmallBlob(FloorDetail? detail) =>
        detail is null;
}
