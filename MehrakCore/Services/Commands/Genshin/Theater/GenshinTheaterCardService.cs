#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Theater;

internal class GenshinTheaterCardService : ICommandService<GenshinTheaterCommandExecutor>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinTheaterCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);

    private readonly Image m_TheaterStarLit;
    private readonly Image m_TheaterStarUnlit;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;

    public GenshinTheaterCardService(ImageRepository imageRepository, ILogger<GenshinTheaterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;
    }

    public async Task<Stream> GetTheaterCardAsync(GenshinTheaterInformation theaterData, UserGameData userGameData,
        Dictionary<int, int> constMap, Dictionary<string, Stream> buffMap)
    {
        return new MemoryStream();
    }
}
