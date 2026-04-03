using Mehrak.Domain.Enums;

namespace Mehrak.Bot.Services.Abstractions;

public interface IBotLocalizationService
{
    string Get(string key, params object[] arguments);
    string Get(WikiLocales locale, string key, params object[] arguments);
}
