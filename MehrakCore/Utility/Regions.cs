#region

#region

using System.ComponentModel;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;

#endregion

namespace MehrakCore.Utility;

#endregion

public class
    RegionsEnumTypeReader<TContext> : ComponentInteractionTypeReader<TContext> where TContext :
    IComponentInteractionContext
{
    public override ValueTask<TypeReaderResult> ReadAsync(ReadOnlyMemory<char> input, TContext context,
        ComponentInteractionParameter<TContext> parameter,
        ComponentInteractionServiceConfiguration<TContext> configuration, IServiceProvider? serviceProvider)
    {
        var inputString = input.ToString().ToLowerInvariant();
        return Enum.TryParse<Regions>(inputString, true, out var region)
            ? new ValueTask<TypeReaderResult>(TypeReaderResult.Success(region))
            : new ValueTask<TypeReaderResult>(TypeReaderResult.ParseFail("Invalid region"));
    }
}

public enum Regions
{
    America,
    Europe,
    Asia,

    [SlashCommandChoice(Name = "TW/HK/MO")] [Description("TW/HK/MO")]
    Sar
}