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
    public List<SkinImageSlot> ImageSlots { get; } = [];
    public List<SkinFontSlot> FontSlots { get; } = [];
    public List<CustomFileRule> CustomFileRules { get; } = [];
    public List<CustomOptionDefinition> CustomOptions { get; } = [];
    public List<CustomFileDefinition> CustomFiles { get; } = [];
    public List<string> Diagnostics { get; } = [];
    public HashSet<int> ActiveOptions { get; } = [];
    public SkinHeaderInfo Header { get; set; } = SkinHeaderInfo.Empty;
    public ResolutionInfo Resolution { get; set; } = ResolutionInfo.Default;
    internal bool HasResolutionCommand { get; set; }
}
