using NetCord.Services.ApplicationCommands;

namespace Mehrak.Bot.Models;

public enum WikiLocaleChoice
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

public static class WikiLocaleChoiceExtensions
{
    public static Domain.Enums.WikiLocales ToDomainLocale(this WikiLocaleChoice choice) => choice switch
    {
        WikiLocaleChoice.EN => Domain.Enums.WikiLocales.EN,
        WikiLocaleChoice.CN => Domain.Enums.WikiLocales.CN,
        WikiLocaleChoice.TW => Domain.Enums.WikiLocales.TW,
        WikiLocaleChoice.JP => Domain.Enums.WikiLocales.JP,
        WikiLocaleChoice.KR => Domain.Enums.WikiLocales.KR,
        WikiLocaleChoice.DE => Domain.Enums.WikiLocales.DE,
        WikiLocaleChoice.ES => Domain.Enums.WikiLocales.ES,
        WikiLocaleChoice.FR => Domain.Enums.WikiLocales.FR,
        WikiLocaleChoice.ID => Domain.Enums.WikiLocales.ID,
        WikiLocaleChoice.IT => Domain.Enums.WikiLocales.IT,
        WikiLocaleChoice.PT => Domain.Enums.WikiLocales.PT,
        WikiLocaleChoice.RU => Domain.Enums.WikiLocales.RU,
        WikiLocaleChoice.TH => Domain.Enums.WikiLocales.TH,
        WikiLocaleChoice.TR => Domain.Enums.WikiLocales.TR,
        WikiLocaleChoice.VN => Domain.Enums.WikiLocales.VN,
        _ => Domain.Enums.WikiLocales.EN
    };
}
