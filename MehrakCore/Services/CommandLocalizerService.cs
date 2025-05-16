#region

using System.Globalization;
using System.Resources;
using MehrakCore.Resources.Modules;
using Microsoft.Extensions.Localization;

#endregion

namespace MehrakCore.Services;

public class CommandLocalizerService
{
    private const string DefaultString = "Localization not found, please report this issue to the developers.";

    private readonly IStringLocalizer<CommandLocalizerService> m_Localizer;
    private readonly ResourceManager m_ResourceManager;

    private static readonly CultureInfo DefaultCulture = new("en-US");

    public CommandLocalizerService(IStringLocalizer<CommandLocalizerService> localizer)
    {
        m_Localizer = localizer;
        m_ResourceManager = new ResourceManager(typeof(CommandStrings));
    }

    public string GetLocalizedString(string key, CultureInfo? culture)
    {
        return m_ResourceManager.GetString(key, culture ?? DefaultCulture) ??
               m_ResourceManager.GetString(key, DefaultCulture) ?? DefaultString;
    }
}
