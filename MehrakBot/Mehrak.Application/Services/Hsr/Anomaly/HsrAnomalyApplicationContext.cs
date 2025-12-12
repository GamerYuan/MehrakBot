using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Hsr.Anomaly;

public class HsrAnomalyApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
    : ApplicationContextBase(userId, parameters)
{
}
