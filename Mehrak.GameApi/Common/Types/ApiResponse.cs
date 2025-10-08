namespace Mehrak.GameApi.Common.Types;

public class ApiResponse<T> where T : class
{
    public T? Data { get; init; }
    public int Retcode { get; init; }
    public required string Message { get; init; }
}
