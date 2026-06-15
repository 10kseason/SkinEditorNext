using IOPath = System.IO.Path;

namespace SkinEditorNext.Models;

public sealed record SkinImageSlot(
    int Index,
    string RawPath,
    string ResolvedPath,
    string SourceFile,
    int SourceLine)
{
    public string DisplayName
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(RawPath) ? ResolvedPath : RawPath;
            var name = IOPath.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) name = path;
            return $"#{Index} {name}";
        }
    }
}
