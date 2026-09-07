namespace Mehrak.Domain.Character;

public sealed class CacheSynchronizationException : Exception
{
    public CacheSynchronizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public bool DatabaseCommitted => true;
}
