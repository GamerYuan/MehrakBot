namespace Mehrak.GameApi.Common;

/// <summary>
/// Centralized log messages for API services
/// </summary>
internal static class LogMessages
{
    // Request logging
    public const string SendingRequest = "Sending request to {Endpoint}";
    public const string ReceivedRequest = "Received request to {Endpoint}";

    // HTTP wire logs (no headers)
    public const string OutboundHttpRequest = "Sending HTTP {Method} to {Endpoint}";
    public const string InboundHttpResponse = "Received HTTP {StatusCode} from {Endpoint}";
    public const string InboundHttpResponseWithRetcode =
        "Received HTTP {StatusCode} from {Endpoint} with API retcode {Retcode} for gameUid: {GameUid}";

    // Success logging
    public const string SuccessfullyRetrievedData =
        "Successfully retrieved data from {Endpoint} for gameUid: {GameUid}";

    public const string SuccessfullyCachedData = "Cached data for game UID: {GameUid} for {Minutes} minutes";
    public const string SuccessfullyRetrievedFromCache = "Retrieved data from cache for game UID: {GameUid}";

    // Error logging - Known retcodes
    public const string InvalidCredentials = "Invalid credentials (retcode10001) for gameUid: {GameUid}";
    public const string AlreadyCheckedIn = "User LtUid: {LtUid} has already checked in today for game {Game}";
    public const string NoValidProfile = "User LtUid: {LtUid} does not have a valid account for game {Game}";
    public const string InvalidRegionOrUid = "Game UID or region is null or empty";

    // Error logging - General
    public const string NonSuccessStatusCode =
        "API returned non-success status code: {StatusCode} for endpoint: {Endpoint}";

    public const string FailedToParseResponse = "Failed to parse JSON response from {Endpoint} for gameUid: {GameUid}";

    public const string UnknownRetcode =
        "API returned non-zero retcode {Retcode} for gameUid: {GameUid} at endpoint: {Endpoint}";

    public const string ExceptionOccurred =
        "An exception occurred while fetching data from {Endpoint} for gameUid: {GameUid}";
}
