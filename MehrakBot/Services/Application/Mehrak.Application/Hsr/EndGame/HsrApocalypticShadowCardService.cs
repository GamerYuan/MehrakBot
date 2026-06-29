#region

using System.Text;
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
        var backgroundTask = Task.Run(async () => await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.ASBackgroundName, cancellationToken),
            cancellationToken), cancellationToken);

        var checkmarkTask = Task.Run(async () => await Image.LoadAsync(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.BossCheckName, cancellationToken),
            cancellationToken), cancellationToken);

        await Task.WhenAll(backgroundTask, checkmarkTask);

        m_AsBackground = await backgroundTask;
        m_BossCheckmark = await checkmarkTask;
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
                        .Where(x => x.MazeId.ToString().EndsWith((floorIndex + 1).ToString()))
                        .OrderByDescending(x => x.StarNum + x.ExtraStarNum)
                        .ThenByDescending(x => x.TotalScore)
                        .ThenByDescending(x => x.IsTierce)
                        .FirstOrDefault();
                    return (FloorNumber: floorIndex, Data: floorData);
                })
        ];
    }

    protected override string GetStageText(HsrEndInformation gameModeData, HsrEndFloorDetail? floorData, int floorNumber)
    {
        var strBuilder = new StringBuilder();
        strBuilder.Append($"{gameModeData.Groups[0].Name}: Difficulty {floorNumber + 1}");
        if (floorNumber == 3)
        {
            if (floorData?.IsTierce == true)
            {
                strBuilder.Append(" Starward Mode");
            }
            else
            {
                strBuilder.Append(" Regular Mode");
            }
        }
        return strBuilder.ToString();
    }

    protected override void DrawNodeExtras(DrawingCanvas region, HsrEndNodeInformation nodeData)
    {
        if (nodeData.BossDefeated)
            region.DrawImage(m_BossCheckmark, m_BossCheckmark.Bounds,
                new RectangleF(605, 0, m_BossCheckmark.Width, m_BossCheckmark.Height),
                KnownResamplers.Bicubic);
    }
}
