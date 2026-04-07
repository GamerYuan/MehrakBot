#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;

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
