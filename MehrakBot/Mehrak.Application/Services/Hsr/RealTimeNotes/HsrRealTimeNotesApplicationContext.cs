using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

public class HsrRealTimeNotesApplicationContext(ulong userId, params (string, object)[] parameters)
    : ApplicationContextBase(userId, parameters)
{
}
