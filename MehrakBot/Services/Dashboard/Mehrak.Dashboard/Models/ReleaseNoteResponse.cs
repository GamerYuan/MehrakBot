namespace Mehrak.Dashboard.Models;

public sealed class ReleaseNoteResponse
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<ReleaseNoteSectionResponse> Sections { get; set; } = [];
}

public sealed class ReleaseNoteSectionResponse
{
    public string Name { get; set; } = string.Empty;
    public List<ReleaseNoteEntryResponse> Notes { get; set; } = [];
}

public sealed class ReleaseNoteEntryResponse
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
