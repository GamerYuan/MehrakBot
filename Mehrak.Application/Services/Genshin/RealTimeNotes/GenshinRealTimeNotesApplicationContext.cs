using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.RealTimeNotes;

public class GenshinRealTimeNotesApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
