namespace SkinEditorNext.Models;

public sealed class SkinIfBlockView
{
    public required string Header { get; init; }
    public required string Summary { get; init; }
    public required string Detail { get; init; }
    public required string SourcePath { get; init; }
    public required int SourceLineNumber { get; init; }
    public List<SkinIfBlockView> Children { get; } = [];
}
