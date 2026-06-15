namespace SkinEditorNext.Models;

public sealed record CustomFileDefinition(
    string Title,
    string Pattern,
    string Selected,
    string SourceFile,
    int SourceLine);
