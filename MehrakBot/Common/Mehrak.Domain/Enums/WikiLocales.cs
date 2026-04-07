namespace Mehrak.Domain.Enums;

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

public static class WikiLocaleExtensions
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
