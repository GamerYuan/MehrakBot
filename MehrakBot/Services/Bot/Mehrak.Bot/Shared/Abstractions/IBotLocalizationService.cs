using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Bot.Shared.Abstractions;

public interface IBotLocalizationService
{
    string Get(string key, params object[] arguments);
    string Get(WikiLocales locale, string key, params object[] arguments);
}
