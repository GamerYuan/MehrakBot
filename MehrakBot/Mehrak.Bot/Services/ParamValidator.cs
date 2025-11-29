#region

using Mehrak.Domain.Services.Abstractions;

#endregion

namespace Mehrak.Bot.Services;

internal abstract class ParamValidator
{
    public string ParamName { get; }
    public string ErrorMessage { get; }

    protected ParamValidator(string paramName, string errorMessage)
    {
        ParamName = paramName;
        ErrorMessage = errorMessage;
    }

    // Abstract method to validate a parameter (passed as object)
    public abstract bool IsValid(IApplicationContext context);
}

// Generic implementation
internal class ParamValidator<TParam> : ParamValidator
{
    private readonly Predicate<TParam> m_Predicate;

    public ParamValidator(string paramName, Predicate<TParam> predicate, string? errorMessage = null)
        : base(paramName, errorMessage ?? $"{paramName} cannot be empty")
    {
        m_Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override bool IsValid(IApplicationContext context)
    {
        var param = context.GetParameter<TParam>(ParamName);
        if (param is not TParam typedParam) return false;

        return m_Predicate(typedParam);
    }
}
