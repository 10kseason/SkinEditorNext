namespace SkinEditorNext.Models;

public sealed record SkinFontSlot(
    int Index,
    string Kind,
    string RawDefinition,
    string SourceFile,
    int SourceLine)
{
    public string DisplayName => $"#{Index} {Kind}";
}
