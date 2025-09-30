using MehrakCore.ApiResponseTypes;
using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace MehrakCore.Services.Commands.Zzz.Assault;

public class ZzzAssaultApiService : IApiService<ZzzAssaultCommandExecutor>, IHostedService
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/mem_detail";
    private const int BossImageHeight = 230;
    private const int BuffImageHeight = 80;

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzAssaultApiService> m_Logger;

    private readonly ConcurrentDictionary<string, Stream> m_BossImage = [];
    private readonly ConcurrentDictionary<string, Stream> m_BuffImage = [];
    private Timer? m_CleanupTimer;

    public ZzzAssaultApiService(IHttpClientFactory clientFactory, ILogger<ZzzAssaultApiService> logger)
    {
        m_HttpClientFactory = clientFactory;
        m_Logger = logger;
    }

    public async Task<ZzzAssaultData> GetAssaultDataAsync(string ltoken, ulong ltuid, string gameUid, string region)
    {
        HttpClient client = m_HttpClientFactory.CreateClient("Default");
        HttpRequestMessage request = new(HttpMethod.Get,
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={region}&uid={gameUid}&schedule_type=1");
        request.Headers.Add("Cookie", $"ltoken_v2={ltoken}; ltuid_v2={ltuid};");
        HttpResponseMessage response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to fetch Zzz Assault data for gameUid: {GameUid}, Status Code: {StatusCode}",
                gameUid, response.StatusCode);
            throw new CommandException("An unknown error occurred when accessing HoYoLAB API. Please try again later");
        }

        ApiResponse<ZzzAssaultData>? json =
            await response.Content.ReadFromJsonAsync<ApiResponse<ZzzAssaultData>>();

        if (json == null)
        {
            m_Logger.LogError("Failed to fetch Zzz Assault data for gameUid: {GameUid}, Status Code: {StatusCode}",
                gameUid, response.StatusCode);
            throw new CommandException("An unknown error occurred when accessing HoYoLAB API. Please try again later");
        }

        if (json.Retcode == 10001)
        {
            m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
            throw new CommandException("Invalid HoYoLAB UID or Cookies. Please authenticate again");
        }

        if (json.Retcode != 0)
        {
            m_Logger.LogWarning("Failed to fetch Zzz Assault data for {GameUid}, Retcode {Retcode}, Message: {Message}",
                gameUid, json?.Retcode, json?.Message);
            throw new CommandException("An error occurred while fetching Deadly Assault data");
        }

        return json.Data!;
    }

    /// <summary>
    /// Asynchronously retrieves and generates the boss image for the specified
    /// assault boss, writing the resulting image to the provided stream.
    /// </summary>
    /// <remarks>
    /// If the boss image has been previously generated and cached, the cached
    /// image is written to the stream. Otherwise, the image is fetched,
    /// composed, cached, and then written. The caller is responsible for
    /// managing the lifetime of the provided stream.
    /// </remarks>
    /// <param name="bossData">
    /// The assault boss data containing information required to fetch and
    /// compose the boss image.
    /// </param>
    /// <param name="stream">
    /// The stream to which the generated boss image will be written. The stream
    /// must be writable.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="CommandException">
    /// Thrown if the boss image or background image cannot be fetched from the
    /// remote server.
    /// </exception>
    public async Task GetBossImageAsync(AssaultBoss bossData, Stream stream)
    {
        if (m_BossImage.TryGetValue(bossData.Name, out Stream? value))
        {
            value.Position = 0;
            await value.CopyToAsync(stream);
            value.Position = 0;
            stream.Position = 0;
            return;
        }

        HttpClient client = m_HttpClientFactory.CreateClient("Default");
        HttpResponseMessage iconResponse = await client.GetAsync(bossData.Icon);
        if (!iconResponse.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to fetch boss image from URL: {IconUrl}, Status Code: {StatusCode}",
                bossData.Icon, iconResponse.StatusCode);
            throw new CommandException("An error occurred while fetching Deadly Assault boss image");
        }

        HttpResponseMessage backgroundResponse = await client.GetAsync(bossData.BgIcon);
        if (!backgroundResponse.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to fetch boss background image from URL: {BgIconUrl}, Status Code: {StatusCode}",
                bossData.BgIcon, backgroundResponse.StatusCode);
            throw new CommandException("An error occurred while fetching Deadly Assault boss background image");
        }

        Image background = await Image.LoadAsync(await backgroundResponse.Content.ReadAsStreamAsync());
        Image icon = await Image.LoadAsync(await iconResponse.Content.ReadAsStreamAsync());
        background.Mutate(ctx =>
        {
            ctx.DrawImage(icon, new Point(0, 0), 1f);
            ctx.Resize(0, BossImageHeight);
            Size size = ctx.GetCurrentSize();
            IPath border = ImageUtility.CreateRoundedRectanglePath(size.Width, BossImageHeight, 15);
            ctx.Draw(Color.Black, 4f, border);
            ctx.ApplyRoundedCorners(15);
        });

        MemoryStream memoryStream = new();
        await background.SaveAsPngAsync(memoryStream);
        memoryStream.Position = 0;
        m_BossImage.AddOrUpdate(bossData.Name, _ => memoryStream, (_, oldStream) => { oldStream.Dispose(); return memoryStream; });

        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(stream);
        memoryStream.Position = 0;
        stream.Position = 0;
    }

    public async Task GetBuffImageAsync(AssaultBuff buff, Stream stream)
    {
        if (m_BuffImage.TryGetValue(buff.Name, out Stream? value))
        {
            value.Position = 0;
            await value.CopyToAsync(stream);
            value.Position = 0;
            stream.Position = 0;
            return;
        }

        HttpClient client = m_HttpClientFactory.CreateClient("Default");
        HttpResponseMessage iconResponse = await client.GetAsync(buff.Icon);
        if (!iconResponse.IsSuccessStatusCode)
        {
            m_Logger.LogError("Failed to fetch buff image from URL: {IconUrl}, Status Code: {StatusCode}",
                buff.Name, iconResponse.StatusCode);
            throw new CommandException("An error occurred while fetching Deadly Assault buff image");
        }
        Stream imageStream = new MemoryStream();
        Image image = await Image.LoadAsync(await iconResponse.Content.ReadAsStreamAsync());
        image.Mutate(ctx => ctx.Resize(0, BuffImageHeight));
        await image.SaveAsPngAsync(imageStream);
        imageStream.Position = 0;
        m_BuffImage.AddOrUpdate(buff.Name, _ => imageStream, (_, oldStream) => { oldStream.Dispose(); return imageStream; });
        await imageStream.CopyToAsync(stream);
        imageStream.Position = 0;
        stream.Position = 0;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        m_CleanupTimer = new Timer(_ => CleanupCache(), null, TimeSpan.Zero, TimeSpan.FromHours(4));
        m_Logger.LogInformation("Service started and cache cleanup timer initialized.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        m_CleanupTimer?.Change(Timeout.Infinite, 0);
        m_Logger.LogInformation("Service stopped and cache cleanup timer disposed.");
        return Task.CompletedTask;
    }

    private void CleanupCache()
    {
        foreach (Stream stream in m_BossImage.Values)
        {
            stream.Dispose();
        }
        m_BossImage.Clear();

        foreach (Stream stream in m_BuffImage.Values)
        {
            stream.Dispose();
        }
        m_BuffImage.Clear();
    }
}
