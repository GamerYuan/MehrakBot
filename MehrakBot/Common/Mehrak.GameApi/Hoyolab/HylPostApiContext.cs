using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.GameApi.Hoyolab;

public class HylPostApiContext : IApiContext
{
    public ulong UserId { get; }
    public long PostId { get; }
    public WikiLocales Locale { get; }

    public HylPostApiContext(ulong userId, long postId, WikiLocales locale = WikiLocales.EN)
    {
        UserId = userId;
        PostId = postId;
        Locale = locale;
    }
}
