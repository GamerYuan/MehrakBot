using System.Collections.Immutable;
using Google.Protobuf.WellKnownTypes;
using Mehrak.Domain.Command.Models;
using Mehrak.Domain.Shared.Enums;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Domain.Command.Extensions;

public static partial class ProtobufMappingExtensions
{
    public static Proto.StorageReference ToProto(this StorageReference domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.StorageReference();
        proto.SourceType = (Proto.AttachmentSourceType)Checked(domain.SourceType);
        proto.StorageFileName = domain.StorageFileName ?? throw new ArgumentException("Missing StorageFileName.", nameof(domain));
        return proto;
    }

    public static StorageReference ToDomain(this Proto.StorageReference proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new StorageReference(
            Checked((AttachmentSourceType)proto.SourceType),
            proto.StorageFileName);
    }

    public static Proto.CommandError ToProto(this CommandError domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.CommandError();
        proto.Code = (Proto.CommandErrorCode)Checked(domain.Code);
        proto.Target = (Proto.CommandErrorTarget)Checked(domain.Target);
        return proto;
    }

    public static CommandError ToDomain(this Proto.CommandError proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new CommandError(
            Checked((CommandErrorCode)proto.Code),
            Checked((CommandErrorTarget)proto.Target));
    }

    public static Proto.CharacterCardItem ToProto(this CharacterCardItem domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.CharacterCardItem();
        proto.RequestedName = domain.RequestedName ?? throw new ArgumentException("Missing RequestedName.", nameof(domain));
        if (domain.CharacterName is not null) proto.CharacterName = domain.CharacterName;
        if (domain.CharacterId is not null) proto.CharacterId = domain.CharacterId.Value;
        proto.Status = (Proto.CharacterCardStatus)Checked(domain.Status);
        if (domain.Image is not null) proto.Image = domain.Image.ToProto();
        if (domain.Error is not null) proto.Error = domain.Error.ToProto();
        return proto;
    }

    public static CharacterCardItem ToDomain(this Proto.CharacterCardItem proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new CharacterCardItem(
            proto.RequestedName,
            proto.HasCharacterName ? proto.CharacterName : null,
            proto.HasCharacterId ? proto.CharacterId : null,
            Checked((CharacterCardStatus)proto.Status),
            proto.Image?.ToDomain(),
            proto.Error?.ToDomain());
    }

    public static Proto.CharacterCardsOutcome ToProto(this CharacterCardsOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.CharacterCardsOutcome();
        proto.Game = domain.Game.ToCommandGame();
        if (domain.GameUid is not null) proto.GameUid = domain.GameUid;
        proto.State = (Proto.OperationOutcome)Checked(domain.State);
        if (domain.Items.IsDefault) throw new ArgumentException("Uninitialized item collection.", nameof(domain));
        proto.Items.AddRange(domain.Items.Select(x => x.ToProto()));
        if (domain.RequestLimit is not null) proto.RequestLimit = domain.RequestLimit.Value;
        return proto;
    }

    public static CharacterCardsOutcome ToDomain(this Proto.CharacterCardsOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new CharacterCardsOutcome(
            proto.Game.ToGame(),
            proto.HasGameUid ? proto.GameUid : null,
            Checked((OperationOutcome)proto.State),
            proto.Items.Select(x => x.ToDomain()).ToImmutableArray(),
            proto.HasRequestLimit ? proto.RequestLimit : null);
    }

    public static Proto.RosterOutcome ToProto(this RosterOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.RosterOutcome();
        proto.Game = domain.Game.ToCommandGame();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.State = (Proto.OperationOutcome)Checked(domain.State);
        if (domain.Image is not null) proto.Image = domain.Image.ToProto();
        return proto;
    }

    public static RosterOutcome ToDomain(this Proto.RosterOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new RosterOutcome(
            proto.Game.ToGame(),
            proto.GameUid,
            Checked((OperationOutcome)proto.State),
            proto.Image?.ToDomain());
    }

    public static Proto.EndgameOutcome ToProto(this EndgameOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.EndgameOutcome();
        proto.Game = domain.Game.ToCommandGame();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.Mode = (Proto.EndgameMode)Checked(domain.Mode);
        proto.State = (Proto.OperationOutcome)Checked(domain.State);
        if (domain.Image is not null) proto.Image = domain.Image.ToProto();
        if (domain.Floor is not null) proto.Floor = domain.Floor.Value;
        if (domain.CycleStart.HasValue) proto.CycleStart = Timestamp.FromDateTimeOffset(domain.CycleStart.Value);
        if (domain.CycleEnd.HasValue) proto.CycleEnd = Timestamp.FromDateTimeOffset(domain.CycleEnd.Value);
        return proto;
    }

    public static EndgameOutcome ToDomain(this Proto.EndgameOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new EndgameOutcome(
            proto.Game.ToGame(),
            proto.GameUid,
            Checked((EndgameMode)proto.Mode),
            Checked((OperationOutcome)proto.State),
            proto.Image?.ToDomain(),
            proto.HasFloor ? proto.Floor : null,
            proto.CycleStart?.ToDateTimeOffset(),
            proto.CycleEnd?.ToDateTimeOffset());
    }

    public static Proto.NotesResource ToProto(this NotesResource domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.NotesResource();
        proto.Current = domain.Current;
        proto.Maximum = domain.Maximum;
        if (domain.RecoveryAt.HasValue) proto.RecoveryAt = Timestamp.FromDateTimeOffset(domain.RecoveryAt.Value);
        return proto;
    }

    public static NotesResource ToDomain(this Proto.NotesResource proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new NotesResource(
            proto.Current,
            proto.Maximum,
            proto.RecoveryAt?.ToDateTimeOffset());
    }

    public static Proto.NotesCounter ToProto(this NotesCounter domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.NotesCounter();
        proto.Current = domain.Current;
        proto.Maximum = domain.Maximum;
        if (domain.ResetAt.HasValue) proto.ResetAt = Timestamp.FromDateTimeOffset(domain.ResetAt.Value);
        return proto;
    }

    public static NotesCounter ToDomain(this Proto.NotesCounter proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new NotesCounter(
            proto.Current,
            proto.Maximum,
            proto.ResetAt?.ToDateTimeOffset());
    }

    public static Proto.ExpeditionNotes ToProto(this ExpeditionNotes domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.ExpeditionNotes();
        proto.Current = domain.Current;
        proto.Maximum = domain.Maximum;
        proto.State = (Proto.ExpeditionOutcome)Checked(domain.State);
        if (domain.CompletionAt.HasValue) proto.CompletionAt = Timestamp.FromDateTimeOffset(domain.CompletionAt.Value);
        return proto;
    }

    public static ExpeditionNotes ToDomain(this Proto.ExpeditionNotes proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new ExpeditionNotes(
            proto.Current,
            proto.Maximum,
            Checked((ExpeditionOutcome)proto.State),
            proto.CompletionAt?.ToDateTimeOffset());
    }

    public static Proto.TransformerNotes ToProto(this TransformerNotes domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.TransformerNotes();
        proto.IsReady = domain.IsReady;
        proto.ResetAt = Timestamp.FromDateTimeOffset(domain.ResetAt);
        return proto;
    }

    public static TransformerNotes ToDomain(this Proto.TransformerNotes proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new TransformerNotes(
            proto.IsReady,
            Required(proto.ResetAt).ToDateTimeOffset());
    }

    public static Proto.GenshinNotesOutcome ToProto(this GenshinNotesOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.GenshinNotesOutcome();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.ObservedAt = Timestamp.FromDateTimeOffset(domain.ObservedAt);
        proto.Resin = domain.Resin.ToProto();
        proto.Expeditions = domain.Expeditions.ToProto();
        proto.HomeCoin = domain.HomeCoin.ToProto();
        proto.RemainingWeeklyDiscounts = domain.RemainingWeeklyDiscounts;
        proto.WeeklyDiscountLimit = domain.WeeklyDiscountLimit;
        proto.WeeklyResetAt = Timestamp.FromDateTimeOffset(domain.WeeklyResetAt);
        if (domain.Transformer is not null) proto.Transformer = domain.Transformer.ToProto();
        return proto;
    }

    public static GenshinNotesOutcome ToDomain(this Proto.GenshinNotesOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new GenshinNotesOutcome(
            proto.GameUid,
            Required(proto.ObservedAt).ToDateTimeOffset(),
            Required(proto.Resin).ToDomain(),
            Required(proto.Expeditions).ToDomain(),
            Required(proto.HomeCoin).ToDomain(),
            proto.RemainingWeeklyDiscounts,
            proto.WeeklyDiscountLimit,
            Required(proto.WeeklyResetAt).ToDateTimeOffset(),
            proto.Transformer?.ToDomain());
    }

    public static Proto.HsrNotesOutcome ToProto(this HsrNotesOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.HsrNotesOutcome();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.ObservedAt = Timestamp.FromDateTimeOffset(domain.ObservedAt);
        proto.TrailblazePower = domain.TrailblazePower.ToProto();
        proto.EchoesOfWarRemaining = domain.EchoesOfWarRemaining;
        proto.EchoesOfWarLimit = domain.EchoesOfWarLimit;
        proto.EchoesOfWarResetAt = Timestamp.FromDateTimeOffset(domain.EchoesOfWarResetAt);
        proto.SimulatedUniverse = domain.SimulatedUniverse.ToProto();
        proto.CurrencyWars = domain.CurrencyWars.ToProto();
        return proto;
    }

    public static HsrNotesOutcome ToDomain(this Proto.HsrNotesOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new HsrNotesOutcome(
            proto.GameUid,
            Required(proto.ObservedAt).ToDateTimeOffset(),
            Required(proto.TrailblazePower).ToDomain(),
            proto.EchoesOfWarRemaining,
            proto.EchoesOfWarLimit,
            Required(proto.EchoesOfWarResetAt).ToDateTimeOffset(),
            Required(proto.SimulatedUniverse).ToDomain(),
            Required(proto.CurrencyWars).ToDomain());
    }

    public static Proto.ZzzNotesOutcome ToProto(this ZzzNotesOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.ZzzNotesOutcome();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.ObservedAt = Timestamp.FromDateTimeOffset(domain.ObservedAt);
        proto.Battery = domain.Battery.ToProto();
        proto.DailyEngagement = domain.DailyEngagement.ToProto();
        proto.ScratchCard = (Proto.ScratchCardOutcome)Checked(domain.ScratchCard);
        proto.VideoStore = (Proto.VideoStoreOutcome)Checked(domain.VideoStore);
        if (domain.BountyCommission is not null) proto.BountyCommission = domain.BountyCommission.ToProto();
        if (domain.WeeklyPoints is not null) proto.WeeklyPoints = domain.WeeklyPoints.ToProto();
        if (domain.Omnicoins is not null) proto.Omnicoins = domain.Omnicoins.ToProto();
        proto.Adventure = (Proto.TempleAdventureOutcome)Checked(domain.Adventure);
        proto.Crafting = (Proto.TempleCraftingOutcome)Checked(domain.Crafting);
        proto.Sales = (Proto.TempleSalesOutcome)Checked(domain.Sales);
        return proto;
    }

    public static ZzzNotesOutcome ToDomain(this Proto.ZzzNotesOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new ZzzNotesOutcome(
            proto.GameUid,
            Required(proto.ObservedAt).ToDateTimeOffset(),
            Required(proto.Battery).ToDomain(),
            Required(proto.DailyEngagement).ToDomain(),
            Checked((ScratchCardOutcome)proto.ScratchCard),
            Checked((VideoStoreOutcome)proto.VideoStore),
            proto.BountyCommission?.ToDomain(),
            proto.WeeklyPoints?.ToDomain(),
            proto.Omnicoins?.ToDomain(),
            Checked((TempleAdventureOutcome)proto.Adventure),
            Checked((TempleCraftingOutcome)proto.Crafting),
            Checked((TempleSalesOutcome)proto.Sales));
    }

    public static Proto.RedemptionItem ToProto(this RedemptionItem domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.RedemptionItem();
        proto.Code = domain.Code ?? throw new ArgumentException("Missing Code.", nameof(domain));
        proto.Outcome = (Proto.RedemptionOutcome)Checked(domain.Outcome);
        if (domain.Error is not null) proto.Error = domain.Error.ToProto();
        return proto;
    }

    public static RedemptionItem ToDomain(this Proto.RedemptionItem proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new RedemptionItem(
            proto.Code,
            Checked((RedemptionOutcome)proto.Outcome),
            proto.Error?.ToDomain());
    }

    public static Proto.CodeRedemptionOutcome ToProto(this CodeRedemptionOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.CodeRedemptionOutcome();
        proto.Game = domain.Game.ToCommandGame();
        proto.GameUid = domain.GameUid ?? throw new ArgumentException("Missing GameUid.", nameof(domain));
        proto.State = (Proto.OperationOutcome)Checked(domain.State);
        if (domain.Items.IsDefault) throw new ArgumentException("Uninitialized item collection.", nameof(domain));
        proto.Items.AddRange(domain.Items.Select(x => x.ToProto()));
        return proto;
    }

    public static CodeRedemptionOutcome ToDomain(this Proto.CodeRedemptionOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new CodeRedemptionOutcome(
            proto.Game.ToGame(),
            proto.GameUid,
            Checked((OperationOutcome)proto.State),
            proto.Items.Select(x => x.ToDomain()).ToImmutableArray());
    }

    public static Proto.CheckInItem ToProto(this CheckInItem domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.CheckInItem();
        proto.Game = domain.Game.ToCommandGame();
        proto.Outcome = (Proto.CheckInOutcome)Checked(domain.Outcome);
        if (domain.Error is not null) proto.Error = domain.Error.ToProto();
        return proto;
    }

    public static CheckInItem ToDomain(this Proto.CheckInItem proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new CheckInItem(
            proto.Game.ToGame(),
            Checked((CheckInOutcome)proto.Outcome),
            proto.Error?.ToDomain());
    }

    public static Proto.DailyCheckInOutcome ToProto(this DailyCheckInOutcome domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        var proto = new Proto.DailyCheckInOutcome();
        proto.State = (Proto.OperationOutcome)Checked(domain.State);
        if (domain.Items.IsDefault) throw new ArgumentException("Uninitialized item collection.", nameof(domain));
        proto.Items.AddRange(domain.Items.Select(x => x.ToProto()));
        return proto;
    }

    public static DailyCheckInOutcome ToDomain(this Proto.DailyCheckInOutcome proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        return new DailyCheckInOutcome(
            Checked((OperationOutcome)proto.State),
            proto.Items.Select(x => x.ToDomain()).ToImmutableArray());
    }

}
