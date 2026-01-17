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

    public abstract bool IsValid(IReadOnlyDictionary<string, object> parameters);
}

internal sealed class ParamValidator<TParam> : ParamValidator
{
    private readonly Predicate<TParam> m_Predicate;

    public ParamValidator(string paramName, Predicate<TParam> predicate, string? errorMessage = null)
        : base(paramName, errorMessage ?? $"{paramName} cannot be empty")
    {
        m_Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public override bool IsValid(IReadOnlyDictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue(ParamName, out var value) || value is not TParam typedValue)
            return false;

        return m_Predicate(typedValue);
    }
}
