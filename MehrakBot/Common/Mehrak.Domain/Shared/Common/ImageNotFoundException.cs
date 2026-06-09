namespace Mehrak.Domain.Shared.Common;


public sealed class ImageNotFoundException : Exception
{
    public string FileName { get; }

    public ImageNotFoundException(string fileName) : base($"Image '{fileName}' not found in storage.")
    {
        FileName = fileName;
    }
}

