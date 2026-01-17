#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Models;

public class Result<T>
{
    public StatusCode StatusCode { get; init; }
    public T? Data { get; init; }

    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess => StatusCode == StatusCode.OK;

    public string? ErrorMessage { get; init; }
    public int? RetCode { get; init; }
    public string? RequestUri { get; init; }

    public static Result<T> Success(T data, int retCode = 0, StatusCode statusCode = StatusCode.OK, string? requestUri = null)
    {
        return new Result<T>
        {
            Data = data,
            StatusCode = statusCode,
            RetCode = retCode,
            RequestUri = requestUri
        };
    }

    public static Result<T> Failure(StatusCode statusCode, string? errorMessage = null, string? requestUri = null)
    {
        return new Result<T>
        {
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            RequestUri = requestUri
        };
    }
}

public enum StatusCode
{
    OK = 0,
    BadParameter = 400,
    Unauthorized = 401,
    BotError = 500,
    ExternalServerError = 600
}
