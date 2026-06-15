namespace SkinEditorNext.Models;

public readonly record struct ResolutionInfo(int Width, int Height)
{
    public static ResolutionInfo Default { get; } = new(640, 480);

    public bool IsValid => Width > 0 && Height > 0;

    public override string ToString() => $"{Width} x {Height}";
}
