using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Models.Abstractions;

namespace Mehrak.Application.Services.Genshin.Types;

public class GenshinEndGameGenerationContext<T> : ICardGenerationContext<T>
{
    public ulong UserId { get; }
    public T Data { get; }
    public Server Server { get; }
    public GameProfileDto GameProfile { get; }
    public Dictionary<int, int> ConstMap { get; }
    public uint Floor { get; }

    public GenshinEndGameGenerationContext(ulong userId, uint floor, T data,
        Server server, GameProfileDto gameProfile, Dictionary<int, int> constMap)
    {
        UserId = userId;
        Data = data;
        Server = server;
        GameProfile = gameProfile;
        ConstMap = constMap;
        Floor = floor;
    }
}
