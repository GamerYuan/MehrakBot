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

public enum WikiLocales
{
    EN,
    CN,
    TW,
    JP,
    KR,
    DE,
    ES,
    FR,
    ID,
    IT,
    PT,
    RU,
    TH,
    TR,
    VN
}

internal static class WikiLocaleExtensions
{
    public static string ToLocaleString(this WikiLocales locale) => locale switch
    {
        WikiLocales.EN => "en-us",
        WikiLocales.CN => "zh-cn",
        WikiLocales.TW => "zh-tw",
        WikiLocales.JP => "ja-jp",
        WikiLocales.KR => "ko-kr",
        WikiLocales.DE => "de-de",
        WikiLocales.ES => "es-es",
        WikiLocales.FR => "fr-fr",
        WikiLocales.ID => "id-id",
        WikiLocales.IT => "it-it",
        WikiLocales.PT => "pt-pt",
        WikiLocales.RU => "ru-ru",
        WikiLocales.TH => "th-th",
        WikiLocales.TR => "tr-tr",
        WikiLocales.VN => "vi-vn",
        _ => "en-us"
    };
}
