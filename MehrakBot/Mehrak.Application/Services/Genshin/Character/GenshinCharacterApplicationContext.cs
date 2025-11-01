﻿#region

using Mehrak.Application.Models.Context;

#endregion

namespace Mehrak.Application.Services.Genshin.Character;

public class GenshinCharacterApplicationContext : ApplicationContextBase
{
    public GenshinCharacterApplicationContext(ulong userId, params IEnumerable<(string, object)> parameters)
        : base(userId, parameters)
    {
    }
}