using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Zzz.RealTimeNotes;

public class ZzzRealTimeNotesApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
