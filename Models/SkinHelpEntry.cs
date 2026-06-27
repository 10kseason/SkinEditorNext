namespace SkinEditorNext.Models;

public sealed class SkinHelpEntry
{
    public SkinHelpEntry(
        string sourcePath,
        int sourceLineNumber,
        string source,
        string line,
        string group,
        string command,
        string position,
        string arguments,
        string rawLine,
        bool isTemplate,
        string detail)
    {
        SourcePath = sourcePath;
        SourceLineNumber = sourceLineNumber;
        Source = source;
        Line = line;
        Group = group;
        Command = command;
        Position = position;
        Arguments = arguments;
        RawLine = rawLine;
        IsTemplate = isTemplate;
        Detail = detail;
    }

    public string SourcePath { get; }

    public int SourceLineNumber { get; }

    public string Source { get; set; }

    public string Line { get; set; }

    public string Group { get; set; }

    public string Command { get; set; }

    public string Position { get; set; }

    public string Arguments { get; set; }

    public string RawLine { get; set; }

    public bool IsTemplate { get; }

    public string Detail { get; set; }

    public bool CanEdit => !string.IsNullOrWhiteSpace(SourcePath) && SourceLineNumber > 0;
}
