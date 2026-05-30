namespace Mehrak.Bot.Shared.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class HelpIgnoreAttribute : Attribute;
