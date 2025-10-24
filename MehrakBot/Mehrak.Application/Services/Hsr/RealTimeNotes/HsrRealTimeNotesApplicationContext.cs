using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.RealTimeNotes;

public class HsrRealTimeNotesApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
