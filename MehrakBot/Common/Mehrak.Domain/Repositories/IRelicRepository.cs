namespace Mehrak.Domain.Repositories;

public interface IRelicRepository
{
    Task AddSetName(int setId, string setName);

    Task<string> GetSetName(int setId);
}