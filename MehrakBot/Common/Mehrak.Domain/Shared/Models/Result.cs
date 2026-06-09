#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Shared.Models;

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

    public static Result<T> FromCancellation(CancellationToken cancellationToken, string? requestUri = null)
    {
        return cancellationToken.IsCancellationRequested
            ? Failure(StatusCode.Cancelled, "Request was cancelled", requestUri)
            : Failure(StatusCode.Timeout, "Request timed out", requestUri);
    }
}

public enum StatusCode
{
    OK = 0,
    BadParameter = 400,
    Unauthorized = 401,
    Cancelled = 499,
    BotError = 500,
    Timeout = 504,
    ExternalServerError = 600
}
