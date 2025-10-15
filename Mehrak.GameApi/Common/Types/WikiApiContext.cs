using Mehrak.Domain.Enums;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.GameApi.Common.Types;

public class WikiApiContext : IApiContext
{
    public ulong UserId { get; }
    public Game Game { get; }
    public string EntryPage { get; }

    public WikiApiContext(ulong userId, Game game, string entryPage)
    {
        UserId = userId;
        Game = game;
        EntryPage = entryPage;
    }
}
