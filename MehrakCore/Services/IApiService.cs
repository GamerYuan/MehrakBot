namespace MehrakCore.Services;

public interface IApiService
{
    public Task<IEnumerable<(string, bool)>> GetApiStatusAsync();
}
