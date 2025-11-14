using Mehrak.Application.Services.Hi3.Types;

namespace Mehrak.Application.Services.Hi3.Character;

public class Hi3CharacterApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters) :
    Hi3ApplicationContextBase(userId, parameters)
{
}
