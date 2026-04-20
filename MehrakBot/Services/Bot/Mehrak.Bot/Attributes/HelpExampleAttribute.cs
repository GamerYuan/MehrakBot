namespace Mehrak.Bot.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
internal sealed class HelpExampleAttribute : Attribute
{
    public HelpExampleAttribute(params string[] values)
    {
        Values = values;
    }

    public string[] Values { get; }
}
