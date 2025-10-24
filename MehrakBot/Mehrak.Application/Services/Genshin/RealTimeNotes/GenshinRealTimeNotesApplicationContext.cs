using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

public class GenshinRealTimeNotesApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
