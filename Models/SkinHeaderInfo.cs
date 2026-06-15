namespace SkinEditorNext.Models;

public sealed record SkinHeaderInfo(
    int Type,
    string Title,
    string Maker,
    string Thumbnail,
    int InformationP5,
    int TargetWidth,
    int TargetHeight)
{
    public static SkinHeaderInfo Empty { get; } = new(
        0,
        string.Empty,
        string.Empty,
        string.Empty,
        0,
        0,
        0);
}
