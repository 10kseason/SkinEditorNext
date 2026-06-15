namespace SkinEditorNext.Models;

public sealed class SkinCommandLine
{
    public required string SourcePath { get; init; }
    public required int SourceLine { get; init; }
    public required int DisplayLine { get; init; }
    public required string RawText { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public required bool IsMainFile { get; init; }

    public string Command => Fields.Count > 0 ? Fields[0] : string.Empty;
    public bool IsCommand => Command.StartsWith('#');
}
