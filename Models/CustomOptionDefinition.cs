namespace SkinEditorNext.Models;

public sealed record CustomOptionDefinition(
    string Title,
    int StartOption,
    IReadOnlyList<string> Labels,
    string SourceFile,
    int SourceLine);
