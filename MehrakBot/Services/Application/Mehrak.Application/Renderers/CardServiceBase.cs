#region

using System.Text.Json;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

#endregion

namespace Mehrak.Application.Renderers;

public record FontDefinitions(
    Font Title,
    Font Normal,
    Font Medium,
    Font Small,
    Font Tiny);

public sealed class DisposableBag : IDisposable
{
    private readonly List<IDisposable> m_Items = [];

    public void Add(IDisposable item) => m_Items.Add(item);
    public void Add<T>(T item) where T : IDisposable => m_Items.Add(item);
    public void AddRange(IEnumerable<IDisposable> items) => m_Items.AddRange(items);

    public void Dispose()
    {
        foreach (var item in m_Items) item.Dispose();
        m_Items.Clear();
    }
}

public abstract class CardServiceBase<TData> : ICardService<TData>, IAsyncInitializable
{
    protected readonly IImageRepository ImageRepository;
    protected readonly ILogger Logger;
    protected readonly IApplicationMetrics Metrics;

    protected readonly FontDefinitions Fonts;

    protected static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    protected static readonly Color OverlayColor = Color.FromRgba(0, 0, 0, 128);
    protected static readonly Color DarkOverlayColor = Color.FromRgba(0, 0, 0, 200);

    private readonly string m_CardTypeName;

    protected Image<Rgba32>? StaticBackground;

    protected CardServiceBase(
        string cardTypeName,
        IImageRepository imageRepository,
        ILogger logger,
        IApplicationMetrics metrics,
        FontDefinitions fonts)
    {
        m_CardTypeName = cardTypeName;
        ImageRepository = imageRepository;
        Logger = logger;
        Metrics = metrics;
        Fonts = fonts;
    }

    public abstract Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default);

    public abstract Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<TData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default);

    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadStaticResourcesAsync(cancellationToken);
        Logger.LogInformation(LogMessage.ServiceInitialized, GetType().Name);
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<TData> context)
    {
        using var timer = Metrics.ObserveCardGenerationDuration(m_CardTypeName.ToLowerInvariant());
        Logger.LogInformation(LogMessage.CardGenStartInfo, m_CardTypeName, context.UserId);

        var disposables = new DisposableBag();
        MemoryStream stream = new();

        try
        {
            var background = CreateBackground();
            disposables.Add(background);

            await RenderCardAsync(background, context, disposables, default);

            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;

            Logger.LogInformation(LogMessage.CardGenSuccess, m_CardTypeName, context.UserId);
            return stream;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, LogMessage.CardGenError, m_CardTypeName, context.UserId,
                JsonSerializer.Serialize(context.Data));

            if (ex is CommandException) throw;

            throw new CommandException($"Failed to generate {m_CardTypeName} card", ex);
        }
        finally
        {
            disposables.Dispose();
        }
    }

    protected virtual Image<Rgba32> CreateBackground()
    {
        if (StaticBackground != null)
        {
            return StaticBackground.CloneAs<Rgba32>();
        }
        return new Image<Rgba32>(1920, 1080);
    }

    protected static FontDefinitions LoadFonts(string fontPath, float titleSize, float normalSize, float? mediumSize = null, float? smallSize = null, float? tinySize = null)
    {
        FontCollection collection = new();
        var family = collection.Add(fontPath);

        var actualMedium = mediumSize ?? normalSize - 4;
        var actualSmall = smallSize ?? actualMedium - 4;
        var actualTiny = tinySize ?? actualSmall - 4;

        return new FontDefinitions(
            Title: family.CreateFont(titleSize, FontStyle.Bold),
            Normal: family.CreateFont(normalSize, FontStyle.Regular),
            Medium: family.CreateFont(actualMedium, FontStyle.Regular),
            Small: family.CreateFont(actualSmall, FontStyle.Regular),
            Tiny: family.CreateFont(actualTiny, FontStyle.Regular));
    }
}
