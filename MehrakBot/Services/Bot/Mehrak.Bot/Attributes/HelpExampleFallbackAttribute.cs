namespace Mehrak.Bot.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class HelpExampleFallbackAttribute : Attribute
{
    public HelpExampleFallbackAttribute(string parameterName, string value)
    {
        ParameterName = parameterName;
        Value = value;
    }

    public string ParameterName { get; }

    public string Value { get; }
}
