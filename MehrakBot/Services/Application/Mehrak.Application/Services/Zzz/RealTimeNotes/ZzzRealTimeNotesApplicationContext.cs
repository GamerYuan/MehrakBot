#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Zzz.RealTimeNotes;

public class ZzzRealTimeNotesApplicationContext(ulong userId, params IEnumerable<(string, string)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
