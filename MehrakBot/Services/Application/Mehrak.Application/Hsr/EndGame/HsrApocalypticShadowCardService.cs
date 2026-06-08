#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Hsr.EndGame;

internal class HsrApocalypticShadowCardService : HsrEndGameCardServiceBase
{
    private Image m_AsBackground = null!;
    private Image m_BossCheckmark = null!;

    public HsrApocalypticShadowCardService(IImageRepository imageRepository,
        ILogger<HsrApocalypticShadowCardService> logger,
        IApplicationMetrics metrics)
        : base("Hsr AS", imageRepository, logger, metrics)
    {
    }

    protected override async Task LoadModeResourcesAsync(CancellationToken cancellationToken)
    {
        m_AsBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.ASBackgroundName, cancellationToken),
            cancellationToken);

        m_BossCheckmark = await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.BossCheckName, cancellationToken),
            cancellationToken);
        m_BossCheckmark.Mutate(ctx => ctx.Transform(new AffineTransformBuilder().AppendTranslation(new PointF(0, -2))));
    }

    protected override Image GetModeBackgroundImage() => m_AsBackground;

    protected override string GetModeString() => "Apocalyptic Shadow";

    protected override List<(int FloorNumber, HsrEndFloorDetail? Data)> GetFloorDetails(
        HsrEndInformation gameModeData)
    {
        return
        [
            .. Enumerable.Range(0, 4)
                .Select(floorIndex =>
                {
                    var floorData = gameModeData.AllFloorDetail
                        .FirstOrDefault(x => x.Name.EndsWith((floorIndex + 1).ToString()));
                    return (FloorNumber: floorIndex, Data: floorData);
                })
        ];
    }

    protected override string GetStageText(HsrEndInformation gameModeData, int floorNumber)
    {
        return $"{gameModeData.Groups[0].Name}: Difficulty {floorNumber + 1}";
    }

    protected override void DrawNodeExtras(DrawingCanvas region, HsrEndNodeInformation nodeData)
    {
        if (nodeData.BossDefeated)
            region.DrawImage(m_BossCheckmark, m_BossCheckmark.Bounds,
                new RectangleF(605, 0, m_BossCheckmark.Width, m_BossCheckmark.Height),
                KnownResamplers.Bicubic);
    }
}
