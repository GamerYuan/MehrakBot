#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services;

public abstract class ImageUpdaterService<T> where T : ICharacterInformation
{
    protected readonly ImageRepository ImageRepository;
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger<ImageUpdaterService<T>> Logger;

    protected readonly HashSet<int> Cache = [];

    protected ImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<ImageUpdaterService<T>> logger)
    {
        ImageRepository = imageRepository;
        HttpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public abstract Task UpdateDataAsync(T characterInformation);
}
