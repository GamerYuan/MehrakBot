#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

internal class HsrApocalypticShadowCardService : HsrEndGameCardServiceBase
{
    private Image m_AsBackground = null!;

    public HsrApocalypticShadowCardService(IImageRepository imageRepository,
        ILogger<HsrApocalypticShadowCardService> logger,
        IApplicationMetrics metrics)
        : base("Hsr AS", imageRepository, logger, metrics)
    {
    }

    protected override async Task LoadModeResourcesAsync(CancellationToken cancellationToken)
    {
        m_AsBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync("hsr_as_bg", cancellationToken),
            cancellationToken);
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

    protected override void DrawNode1Extras(IImageProcessingContext ctx, int xOffset, int yOffset,
        HsrEndFloorDetail floorData)
    {
        if (floorData.Node1!.BossDefeated)
            ctx.DrawImage(BossCheckmark, new Point(xOffset + 650, yOffset + 83), 1f);
    }

    protected override void DrawNode2Extras(IImageProcessingContext ctx, int xOffset, int yOffset,
        HsrEndFloorDetail floorData)
    {
        if (floorData.Node2!.BossDefeated)
            ctx.DrawImage(BossCheckmark, new Point(xOffset + 650, yOffset + 348), 1f);
    }
}
