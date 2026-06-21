namespace SkinEditorNext.Models;

public sealed class SkinHelpEntry
{
    public SkinHelpEntry(
        string source,
        string line,
        string group,
        string command,
        string arguments,
        string rawLine,
        bool isTemplate,
        string detail)
    {
        Source = source;
        Line = line;
        Group = group;
        Command = command;
        Arguments = arguments;
        RawLine = rawLine;
        IsTemplate = isTemplate;
        Detail = detail;
    }

    public string Source { get; }

    public string Line { get; }

    public string Group { get; }

    public string Command { get; }

    public string Arguments { get; }

    public string RawLine { get; }

    public bool IsTemplate { get; }

    public string Detail { get; }
}