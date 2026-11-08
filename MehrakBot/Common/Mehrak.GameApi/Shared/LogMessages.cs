namespace Mehrak.GameApi.Shared;

/// <summary>
/// Centralized log templates for API services. Every argument is a scalar or a pre-rendered sanitized string; no
/// template destructures a request, response, header collection or exception object.
/// </summary>
internal static class LogMessages
{
    // Request logging
    public const string PreparingRequest = "Preparing request to {Endpoint} with safe query {SafeQueryParameters}";

    // HTTP wire logs (no headers)
    public const string InboundHttpResponseWithRetcode =
        "Received HTTP {StatusCode} from {Endpoint} with API retcode {Retcode} for User {UserId}, safe query {SafeQueryParameters}";

    // Error logging - Known retcodes / auth / parameter issues
    public const string InvalidCredentials = "Invalid credentials for User {UserId}";
    public const string AlreadyCheckedIn = "User {UserId} has already checked in today for game {Game}";
    public const string NoValidProfile = "User {UserId} does not have a valid account for game {Game}";
    public const string InvalidRegionOrUid = "Game UID or region is null or empty";

    // Feature / domain specific states
    public const string FeatureNotUnlocked = "Feature {Feature} is not unlocked for User {UserId}";
    public const string DataNotFoundForFeature = "No data found for feature {Feature} for User {UserId}";

    // Error logging - General failure diagnostics
    public const string NonSuccessStatusCode =
        "API returned non-success status code: {StatusCode} for endpoint: {Endpoint} with safe query {SafeQueryParameters}. Content type: {ContentType}, content length: {ContentLength}. Sanitized response body: {SanitizedResponseBody}";

    public const string FailedToParseResponse =
        "Failed to parse JSON response from {Endpoint} for User {UserId} with safe query {SafeQueryParameters}. Content type: {ContentType}, content length: {ContentLength}. Sanitized response body: {SanitizedResponseBody}";

    public const string EmptyResponseData =
        "Response contained empty data from {Endpoint} for User {UserId} with safe query {SafeQueryParameters}. Sanitized response body: {SanitizedResponseBody}";

    public const string UnknownRetcode =
        "API returned non-zero retcode {Retcode} for User {UserId} at endpoint: {Endpoint} with safe query {SafeQueryParameters}. Sanitized response body: {SanitizedResponseBody}";

    public const string ExceptionOccurred =
        "An exception occurred while fetching data from {Endpoint} for User {UserId}: {ExceptionType}";
}
