namespace MehrakCore.Models;

public class CommandException : Exception
{
    public CommandException(string message) : base(message)
    {
    }

    public CommandException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public CommandException() : base("An unknown error occurred when executing the command")
    {
    }
}
