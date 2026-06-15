namespace SkinEditorNext.Models;

public sealed class SkinObjectView
{
    public int Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string CommandSuffix { get; init; } = string.Empty;
    public string ImagePath { get; init; } = string.Empty;
    public string SourceFile { get; init; } = string.Empty;
    public int SrcLine { get; init; }
    public int DstLine { get; set; }
    public bool IsEditableInMain { get; set; }
    public int SourceIndex { get; init; }
    public int SourceGraph { get; init; }
    public int SourceDivX { get; init; } = 1;
    public int SourceDivY { get; init; } = 1;
    public int SourceCycle { get; init; }
    public int SourceTimer { get; init; }
    public int SourceOp1 { get; init; }
    public int SourceOp2 { get; init; }
    public int SourceOp3 { get; init; }
    public int SourceOp4 { get; init; }
    public int SourceOp5 { get; init; }
    public int DestLoop { get; set; }
    public int DestTimer { get; set; }
    public int DestOp1 { get; set; }
    public int DestOp2 { get; set; }
    public int DestOp3 { get; set; }
    public int DestOp4 { get; set; }
    public int DestOp5 { get; set; }
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public int DestX { get; set; }
    public int DestY { get; set; }
    public int DestWidth { get; set; }
    public int DestHeight { get; set; }
    public List<SkinDstFrame> Frames { get; } = [];

    public string Location => $"{System.IO.Path.GetFileName(SourceFile)}:{SrcLine}";
}
