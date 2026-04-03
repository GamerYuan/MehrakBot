using NetCord.Services.ApplicationCommands;

namespace Mehrak.Domain.Enums;

public enum WikiLocales
{
    [SlashCommandChoice(Name = "English")]
    EN,
    [SlashCommandChoice(Name = "Chinese (Simplified)")]
    CN,
    [SlashCommandChoice(Name = "Chinese (Traditional)")]
    TW,
    [SlashCommandChoice(Name = "Japanese")]
    JP,
    [SlashCommandChoice(Name = "Korean")]
    KR,
    [SlashCommandChoice(Name = "German")]
    DE,
    [SlashCommandChoice(Name = "Spanish")]
    ES,
    [SlashCommandChoice(Name = "French")]
    FR,
    [SlashCommandChoice(Name = "Indonesian")]
    ID,
    [SlashCommandChoice(Name = "Italian")]
    IT,
    [SlashCommandChoice(Name = "Portuguese")]
    PT,
    [SlashCommandChoice(Name = "Russian")]
    RU,
    [SlashCommandChoice(Name = "Thai")]
    TH,
    [SlashCommandChoice(Name = "Turkish")]
    TR,
    [SlashCommandChoice(Name = "Vietnamese")]
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
