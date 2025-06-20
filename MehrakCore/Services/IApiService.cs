namespace MehrakCore.Services;

public interface IApiService<T>
{
    public Task<IEnumerable<(string, bool)>> GetApiStatusAsync();
}
