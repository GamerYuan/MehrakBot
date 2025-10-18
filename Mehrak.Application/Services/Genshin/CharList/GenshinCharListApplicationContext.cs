using Mehrak.Application.Models.Context;

namespace Mehrak.Application.Services.Genshin.CharList;

public class GenshinCharListApplicationContext(ulong userId,
    params (string, object)[] parameters) :
    ApplicationContextBase(userId, parameters)
{
}
