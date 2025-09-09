namespace MehrakCore.ApiResponseTypes;

public class ApiResponse<T> where T : class
{
    public required T Data { get; init; }
    public int Retcode { get; init; }
    public required string Message { get; init; }
}
