namespace Mehrak.Bot.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class HelpNotesAttribute : Attribute
{
    public HelpNotesAttribute(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
