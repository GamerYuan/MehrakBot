using Mehrak.Domain.Shared.Enums;
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
    public static WikiLocales ToDomainLocale(this WikiLocaleChoice choice) => choice switch
    {
        WikiLocaleChoice.EN => WikiLocales.EN,
        WikiLocaleChoice.CN => WikiLocales.CN,
        WikiLocaleChoice.TW => WikiLocales.TW,
        WikiLocaleChoice.JP => WikiLocales.JP,
        WikiLocaleChoice.KR => WikiLocales.KR,
        WikiLocaleChoice.DE => WikiLocales.DE,
        WikiLocaleChoice.ES => WikiLocales.ES,
        WikiLocaleChoice.FR => WikiLocales.FR,
        WikiLocaleChoice.ID => WikiLocales.ID,
        WikiLocaleChoice.IT => WikiLocales.IT,
        WikiLocaleChoice.PT => WikiLocales.PT,
        WikiLocaleChoice.RU => WikiLocales.RU,
        WikiLocaleChoice.TH => WikiLocales.TH,
        WikiLocaleChoice.TR => WikiLocales.TR,
        WikiLocaleChoice.VN => WikiLocales.VN,
        _ => WikiLocales.EN
    };
}
