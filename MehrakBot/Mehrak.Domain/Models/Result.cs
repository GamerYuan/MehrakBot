#region

using System.Diagnostics.CodeAnalysis;

#endregion

namespace Mehrak.Domain.Models;

public class Result<T>
{
    public StatusCode StatusCode { get; set; }
    public T? Data { get; set; }

    [MemberNotNullWhen(true, nameof(Data))]
    [MemberNotNullWhen(false, nameof(ErrorMessage))]
    public bool IsSuccess => StatusCode == StatusCode.OK;

    public string? ErrorMessage { get; set; }
    public int? RetCode { get; set; }

    public static Result<T> Success(T data, int retCode = 0, StatusCode statusCode = StatusCode.OK)
    {
        return new Result<T>
        {
            Data = data,
            StatusCode = statusCode,
            RetCode = retCode
        };
    }

    public static Result<T> Failure(StatusCode statusCode, string? errorMessage = null)
    {
        return new Result<T>
        {
            StatusCode = statusCode,
            ErrorMessage = errorMessage
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