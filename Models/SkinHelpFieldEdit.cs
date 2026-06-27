namespace SkinEditorNext.Models;

public sealed class SkinHelpFieldEdit
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required string Value { get; set; }
    public required string Hint { get; init; }
}