using Mehrak.Infrastructure.Config;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Mehrak.Bot.Services;

public class UserCountTrackerService
{
    private const string Key = "user_count";

    private readonly string m_InstanceName;
    private readonly IDatabase m_Redis;

    public UserCountTrackerService(IOptions<RedisConfig> config, IConnectionMultiplexer conn)
    {
        m_InstanceName = config.Value.InstanceName;
        m_Redis = conn.GetDatabase();
    }

    public async Task AdjustUserCountAsync(int delta)
    {
        await m_Redis.StringIncrementAsync($"{m_InstanceName}{Key}", delta);
    }

    public async Task<int> GetUserCountAsync()
    {
        var value = await m_Redis.StringGetAsync($"{m_InstanceName}{Key}");
        return value.HasValue ? (int)value : 0;
    }
}
