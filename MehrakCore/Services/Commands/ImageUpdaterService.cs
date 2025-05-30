#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands;

public abstract class ImageUpdaterService<T> where T : ICharacterInformation
{
    protected readonly ImageRepository ImageRepository;
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger<ImageUpdaterService<T>> Logger;

    protected const string WikiApi = "https://sg-wiki-api-static.hoyolab.com/hoyowiki/genshin/wapi/entry_page";

    protected ImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<ImageUpdaterService<T>> logger)
    {
        ImageRepository = imageRepository;
        HttpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public abstract Task UpdateDataAsync(T characterInformation, IEnumerable<Dictionary<string, string>> wiki);
}
