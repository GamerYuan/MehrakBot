#region

using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class WikiApiContext : IApiContext
{
    public ulong UserId { get; }
    public Game Game { get; }
    public string EntryPage { get; }
    public WikiLocales Locale { get; }

    public WikiApiContext(ulong userId, Game game, string entryPage, WikiLocales locale = WikiLocales.EN)
    {
        UserId = userId;
        Game = game;
        EntryPage = entryPage;
        Locale = locale;
    }
}
