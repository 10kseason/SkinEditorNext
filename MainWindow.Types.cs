using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using SkinEditorNext.Models;
using SkinEditorNext.Services;
using IOPath = System.IO.Path;

namespace SkinEditorNext;

public partial class MainWindow
{
    private sealed record PreviewCommandCompletion(string Command, string Group)
    {
        public string Label => string.IsNullOrWhiteSpace(Group) ? Command : $"{Command}  {Group}";
    }

    private readonly record struct PreviewCodeCompletionContext(int Start, int Length, string Prefix);
    private sealed record SkinHelpEditSnapshot(
        string SourcePath,
        int SourceLineNumber,
        bool IsTemplate,
        string EditedColumn,
        string Command,
        string Arguments,
        string RawLine)
    {
        public static SkinHelpEditSnapshot Capture(SkinHelpEntry entry, string editedColumn)
        {
            return new SkinHelpEditSnapshot(
                entry.SourcePath,
                entry.SourceLineNumber,
                entry.IsTemplate,
                editedColumn,
                entry.Command,
                entry.Arguments,
                entry.RawLine);
        }

        public bool Matches(SkinHelpEntry entry, string editedColumn)
        {
            return SourceLineNumber == entry.SourceLineNumber &&
                   IsTemplate == entry.IsTemplate &&
                   string.Equals(SourcePath, entry.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(EditedColumn, editedColumn, StringComparison.OrdinalIgnoreCase);
        }

        public bool HasSameValues(SkinHelpEntry entry)
        {
            return string.Equals(Command, entry.Command, StringComparison.Ordinal) &&
                   string.Equals(Arguments, entry.Arguments, StringComparison.Ordinal) &&
                   string.Equals(RawLine, entry.RawLine, StringComparison.Ordinal);
        }
    }

    private sealed record SkinHelpGridPosition(string? SourcePath, int SourceLineNumber, bool IsTemplate, double VerticalOffset, double HorizontalOffset);
    private sealed record SkinImportEntry(string FullPath, string RelativePath, string DisplayName);
    private sealed record PreviewCodeDocumentSpec(string Path, bool IsMain);
    private sealed record PreviewCsvLineUpdate(string Path, int LineNumber, string Csv);

    private sealed class PreviewCodeDocument
    {
        public string Path { get; set; } = string.Empty;
        public Encoding Encoding { get; init; } = Encoding.UTF8;
        public bool IsMain { get; init; }
        public string Text { get; set; } = string.Empty;
        public bool IsDirty { get; set; }
        public TextBox? Editor { get; set; }
        public bool IsCsv => IOPath.GetExtension(Path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }
}
