namespace Mehrak.Application.Models.Context;

public class CheckInApplicationContext(ulong userId, ulong ltUid, string lToken, params (string, object)[] parameters)
    : ApplicationContextBase(userId, ltUid, lToken, parameters)
{
}
