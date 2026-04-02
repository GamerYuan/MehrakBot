using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Common.Types;

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
