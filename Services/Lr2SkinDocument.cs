using System.Text;
using SkinEditorNext.Models;

namespace SkinEditorNext.Services;

public sealed class Lr2SkinDocument
{
    public string MainPath { get; init; } = string.Empty;
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public string MainText { get; set; } = string.Empty;
    public List<SkinCommandLine> Lines { get; } = [];
    public List<SkinObjectView> Objects { get; } = [];
    public List<CustomFileRule> CustomFileRules { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public HashSet<int> ActiveOptions { get; } = [];
    public ResolutionInfo Resolution { get; set; } = ResolutionInfo.Default;
}
