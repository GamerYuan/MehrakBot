using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Mehrak.Domain.Models;

public class ApiResult<T>
{
    public HttpStatusCode StatusCode { get; set; }
    public T? Data { get; set; }

    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess => (int)StatusCode >= 200 && (int)StatusCode < 300;

    public string? ErrorMessage { get; set; }
    public int? RetCode { get; set; }

    public static ApiResult<T> Success(T data, int retCode = 0, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new ApiResult<T>
        {
            Data = data,
            StatusCode = statusCode,
            RetCode = retCode
        };
    }

    public static ApiResult<T> Failure(HttpStatusCode statusCode, string? errorMessage = null)
    {
        return new ApiResult<T>
        {
            StatusCode = statusCode,
            ErrorMessage = errorMessage
        };
    }
}
