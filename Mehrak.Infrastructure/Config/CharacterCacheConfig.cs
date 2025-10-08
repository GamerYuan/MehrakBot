using System;

namespace Mehrak.Infrastructure.Config;

public class CharacterCacheConfig
{
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromHours(1);
    public bool EnablePeriodicUpdates { get; set; } = true;
    public bool EnableInitialPopulation { get; set; } = true;
}