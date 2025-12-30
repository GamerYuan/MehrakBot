using Mehrak.Domain.Services.Abstractions;

namespace Mehrak.Dashboard.Services;

internal abstract class ParamValidator
{
    public string ParamName { get; }
    public string ErrorMessage { get; }

    protected ParamValidator(string paramName, string errorMessage)
    {
        ParamName = paramName;
        ErrorMessage = errorMessage;
    }

    public abstract bool IsValid(IApplicationContext context);
}

internal sealed class ParamValidator<TParam> : ParamValidator
{
    private readonly Predicate<TParam> m_Predicate;

    public ParamValidator(string paramName, Predicate<TParam> predicate, string? errorMessage = null)
        : base(paramName, errorMessage ?? $"{paramName} cannot be empty")
    {
        m_Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override bool IsValid(IApplicationContext context)
    {
        var value = context.GetParameter<TParam>(ParamName);
        if (value is not TParam typedValue)
            return false;

        return m_Predicate(typedValue);
    }
}
