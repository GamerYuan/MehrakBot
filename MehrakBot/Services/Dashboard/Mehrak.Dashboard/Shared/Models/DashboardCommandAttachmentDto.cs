using System.Text.Json.Serialization;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Dashboard.Shared.Models;

public abstract record DashboardCommandAttachmentDto(
    string? StorageFileName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<Game>))] Game Game,
    string? GameUid,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OperationOutcome>))] OperationOutcome State,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CommandErrorCode>))] CommandErrorCode? Code);

public sealed record DashboardRosterDto(string? StorageFileName, Game Game, string GameUid, OperationOutcome State)
    : DashboardCommandAttachmentDto(StorageFileName, Game, GameUid, State, null);

public sealed record DashboardEndgameDto(
    string? StorageFileName, Game Game, string GameUid, OperationOutcome State,
    [property: JsonConverter(typeof(JsonStringEnumConverter<EndgameMode>))] EndgameMode Mode,
    int? Floor, DateTimeOffset? CycleStart, DateTimeOffset? CycleEnd)
    : DashboardCommandAttachmentDto(StorageFileName, Game, GameUid, State, null);

public sealed record DashboardCharacterCardsDto(
    string? StorageFileName, Game Game, string? GameUid, OperationOutcome State, CommandErrorCode? Code,
    IReadOnlyList<DashboardCharacterCardItemDto> Items, int? RequestLimit)
    : DashboardCommandAttachmentDto(StorageFileName, Game, GameUid, State, Code);

public sealed record DashboardCharacterCardItemDto(
    string RequestedName, string? CharacterName, int? CharacterId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CharacterCardStatus>))] CharacterCardStatus Status,
    string? StorageFileName, DashboardCommandErrorDto? Error);

public sealed record DashboardCommandErrorDto(
    string Error,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CommandErrorCode>))] CommandErrorCode Code);
