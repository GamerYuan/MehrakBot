using System.Globalization;
using Mehrak.Bot.Resources;
using Mehrak.Bot.Services.Abstractions;
using Mehrak.Domain.Enums;
using Microsoft.Extensions.Localization;

namespace Mehrak.Bot.Services;

internal class BotLocalizationService : IBotLocalizationService
{
    private readonly IStringLocalizer<Messages> m_Localizer;

    public BotLocalizationService(IStringLocalizer<Messages> localizer)
    {
        m_Localizer = localizer;
    }

    public string Get(string key, params object[] arguments)
    {
        return GetCore(key, arguments);
    }

    public string Get(WikiLocales locale, string key, params object[] arguments)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var culture = ResolveCulture(locale);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            return GetCore(key, arguments);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private string GetCore(string key, params object[] arguments)
    {
        var value = arguments.Length == 0 ? m_Localizer[key] : m_Localizer[key, arguments];
        return value.ResourceNotFound ? key : value.Value;
    }

    private static CultureInfo ResolveCulture(WikiLocales locale)
    {
        return locale switch
        {
            WikiLocales.EN => CultureInfo.GetCultureInfo("en-US"),
            WikiLocales.CN => CultureInfo.GetCultureInfo("zh-CN"),
            WikiLocales.TW => CultureInfo.GetCultureInfo("zh-TW"),
            WikiLocales.JP => CultureInfo.GetCultureInfo("ja-JP"),
            WikiLocales.KR => CultureInfo.GetCultureInfo("ko-KR"),
            WikiLocales.DE => CultureInfo.GetCultureInfo("de-DE"),
            WikiLocales.ES => CultureInfo.GetCultureInfo("es-ES"),
            WikiLocales.FR => CultureInfo.GetCultureInfo("fr-FR"),
            WikiLocales.ID => CultureInfo.GetCultureInfo("id-ID"),
            WikiLocales.IT => CultureInfo.GetCultureInfo("it-IT"),
            WikiLocales.PT => CultureInfo.GetCultureInfo("pt-PT"),
            WikiLocales.RU => CultureInfo.GetCultureInfo("ru-RU"),
            WikiLocales.TH => CultureInfo.GetCultureInfo("th-TH"),
            WikiLocales.TR => CultureInfo.GetCultureInfo("tr-TR"),
            WikiLocales.VN => CultureInfo.GetCultureInfo("vi-VN"),
            _ => CultureInfo.GetCultureInfo("en-US")
        };
    }
}
