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
    private void LoadSkinHelp()
    {
        _skinHelpTemplates.Clear();
        _skinHelpTemplatesByCommand.Clear();
        _skinHelpGroupByCommand.Clear();
        _skinHelpSortOrderByCommand.Clear();
        _skinHelpCommandChoices.Clear();

        var groupPath = FindSkinObjGroupPath();
        if (groupPath is not null)
        {
            LoadSkinObjectGroups(groupPath);
        }

        var path = FindSkinHelperPath();
        if (path is null)
        {
            SkinHelpGrid.ItemsSource = null;
            SkinHelpPathText.Text = groupPath is null ? "skinHelper.txt and skinObjGroup.txt not found." : $"skinHelper.txt not found. Groups: {groupPath}";
            SkinHelpSummaryText.Text = "0 row(s)";
            return;
        }

        try
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                lineNumber++;
                var rawLine = line.Trim();
                if (rawLine.Length == 0) continue;

                var fields = CsvUtil.Split(rawLine);
                if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0])) continue;

                var command = NormalizeSkinHelpCommand(fields[0]);
                var arguments = FormatSkinHelpArguments(fields);
                var entry = new SkinHelpEntry(
                    path,
                    lineNumber,
                    "Helper",
                    lineNumber.ToString(CultureInfo.InvariantCulture),
                    ReadSkinHelpGroup(command),
                    command,
                    string.Empty,
                    arguments,
                    rawLine,
                    isTemplate: true,
                    FormatSkinHelpTemplateDetail(path, lineNumber, fields, rawLine));
                _skinHelpTemplates.Add(entry);
                _skinHelpTemplatesByCommand[command] = entry;

                if (!_skinHelpCommandChoices.Any(existing => string.Equals(existing, command, StringComparison.OrdinalIgnoreCase)))
                {
                    _skinHelpCommandChoices.Add(command);
                }
            }

            _skinHelpCommandChoices.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SkinHelpGrid.ItemsSource = null;
            SkinHelpPathText.Text = $"Failed to read {path}: {ex.Message}";
            SkinHelpSummaryText.Text = "0 row(s)";
            return;
        }

        SkinHelpPathText.Text = groupPath is null ? path : $"{path} / {groupPath}";
        ApplySkinHelpFilter();
    }

    private void RefreshSkinHelpRows()
    {
        _skinHelpRows.Clear();
        if (_document is null)
        {
            ApplySkinHelpFilter();
            return;
        }

        foreach (var line in _document.Lines.Where(line => line.Fields.Count > 0))
        {
            _skinHelpTemplatesByCommand.TryGetValue(line.Command, out var template);
            var source = IOPath.GetFileName(line.SourcePath);
            if (!line.IsMainFile)
            {
                source += " (include)";
            }

            var arguments = FormatSkinHelpArguments(line.Fields);
            var position = FormatSkinHelpPosition(line);
            var detail = FormatSkinHelpDetail(line, template, arguments, position);
            _skinHelpRows.Add(new SkinHelpEntry(
                line.SourcePath,
                line.SourceLine,
                source,
                line.SourceLine.ToString(CultureInfo.InvariantCulture),
                ReadSkinHelpGroup(line.Command),
                line.Command,
                position,
                arguments,
                line.RawText.Trim(),
                isTemplate: false,
                detail));
        }

        ApplySkinHelpFilter();
    }

    private void ApplySkinHelpFilter(bool preservePosition = true)
    {
        if (SkinHelpGrid is null || SkinHelpSearchBox is null || SkinHelpSummaryText is null) return;

        var position = preservePosition ? CaptureSkinHelpGridPosition() : null;
        var showingTemplates = ReadSkinHelpMode() == "templates";
        var sourceRows = showingTemplates ? _skinHelpTemplates : _skinHelpRows;
        var query = SkinHelpSearchBox.Text.Trim();
        List<SkinHelpEntry> filtered = string.IsNullOrWhiteSpace(query)
            ? sourceRows.ToList()
            : sourceRows
                .Where(entry =>
                    entry.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Line.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Command.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Position.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Arguments.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.RawLine.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        filtered = SortSkinHelpRows(filtered);

        SkinHelpGrid.ItemsSource = null;
        SkinHelpGrid.ItemsSource = filtered;
        if (position is not null)
        {
            RestoreSkinHelpGridPosition(position, filtered);
        }

        var label = showingTemplates ? "template" : "skin row";
        SkinHelpSummaryText.Text = $"{filtered.Count:N0} / {sourceRows.Count:N0} {label}(s)";
    }
    private List<SkinHelpEntry> SortSkinHelpRows(List<SkinHelpEntry> rows)
    {
        return rows
            .Select((entry, index) => new { Entry = entry, Index = index })
            .OrderBy(pair => ReadSkinHelpSortOrder(pair.Entry.Command))
            .ThenBy(pair => pair.Index)
            .Select(pair => pair.Entry)
            .ToList();
    }

    private int ReadSkinHelpSortOrder(string command)
    {
        return _skinHelpSortOrderByCommand.TryGetValue(NormalizeSkinHelpCommand(command), out var order)
            ? order
            : int.MaxValue;
    }

    private SkinHelpGridPosition? CaptureSkinHelpGridPosition()
    {
        if (SkinHelpGrid is null)
        {
            return null;
        }

        var entry = SkinHelpGrid.SelectedItem as SkinHelpEntry;
        var scrollViewer = FindVisualDescendant<ScrollViewer>(SkinHelpGrid);
        return new SkinHelpGridPosition(
            entry?.SourcePath,
            entry?.SourceLineNumber ?? 0,
            entry?.IsTemplate ?? false,
            scrollViewer?.VerticalOffset ?? 0,
            scrollViewer?.HorizontalOffset ?? 0);
    }

    private void RestoreSkinHelpGridPosition(SkinHelpGridPosition position, IReadOnlyList<SkinHelpEntry> rows)
    {
        if (SkinHelpGrid is null)
        {
            return;
        }

        var selectedEntry = rows.FirstOrDefault(entry => SkinHelpEntryMatchesPosition(entry, position));
        if (selectedEntry is not null)
        {
            SkinHelpGrid.SelectedItem = selectedEntry;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (selectedEntry is not null)
            {
                SkinHelpGrid.UpdateLayout();
                SkinHelpGrid.ScrollIntoView(selectedEntry);
            }

            var scrollViewer = FindVisualDescendant<ScrollViewer>(SkinHelpGrid);
            if (scrollViewer is null)
            {
                return;
            }

            scrollViewer.ScrollToHorizontalOffset(Math.Min(position.HorizontalOffset, scrollViewer.ScrollableWidth));
            scrollViewer.ScrollToVerticalOffset(Math.Min(position.VerticalOffset, scrollViewer.ScrollableHeight));
        }), DispatcherPriority.Loaded);
    }

    private static bool SkinHelpEntryMatchesPosition(SkinHelpEntry entry, SkinHelpGridPosition position)
    {
        return position.SourcePath is not null &&
               entry.SourceLineNumber == position.SourceLineNumber &&
               entry.IsTemplate == position.IsTemplate &&
               string.Equals(entry.SourcePath, position.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }


    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typedCurrent)
            {
                return typedCurrent;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
    private void InsertSelectedSkinHelpLine()
    {
        if (SkinHelpGrid.SelectedItem is not SkinHelpEntry entry)
        {
            SetStatus("Select a help row first.");
            return;
        }

        var template = entry.IsTemplate
            ? entry
            : _skinHelpTemplatesByCommand.TryGetValue(entry.Command, out var matchedTemplate)
                ? matchedTemplate
                : null;

        if (template is null)
        {
            SetStatus($"No helper template for {entry.Command}.");
            return;
        }

        CodeEditor.Focus();
        var insertion = template.RawLine + Environment.NewLine;
        var insertionStart = CodeEditor.SelectionStart;
        CodeEditor.SelectedText = insertion;
        CodeEditor.CaretIndex = insertionStart + insertion.Length;
        SetStatus($"Inserted {template.Command} template.");
    }

    private void LoadSkinHelpEasyEditor(SkinHelpEntry? entry)
    {
        if (SkinHelpEasyCommandBox is null || SkinHelpEasyFieldsGrid is null || SkinHelpEasySummaryText is null)
        {
            return;
        }

        _loadingSkinHelpEasyEditor = true;
        try
        {
            _skinHelpEasyFields.Clear();
            SkinHelpEasyFieldsGrid.ItemsSource = null;

            if (entry is null)
            {
                SkinHelpEasySummaryText.Text = "왼쪽에서 줄을 고르세요.";
                SkinHelpEasyCommandBox.Text = string.Empty;
                SkinHelpEasyCsvBox.Text = string.Empty;
                SetSkinHelpEasyButtonsEnabled(false);
                return;
            }

            SkinHelpEasySummaryText.Text = entry.IsTemplate
                ? $"템플릿 {entry.Line}번"
                : $"{entry.Source} {entry.Line}번";
            SkinHelpEasyCommandBox.Text = entry.Command;
            LoadSkinHelpEasyFields(entry.Command, CsvUtil.Split(entry.RawLine), preserveValues: null);
            SkinHelpEasyCsvBox.Text = entry.RawLine;
            SetSkinHelpEasyButtonsEnabled(true);
        }
        finally
        {
            _loadingSkinHelpEasyEditor = false;
        }
    }

    private void RefreshSkinHelpEasyFieldsForCommand()
    {
        if (SkinHelpEasyCommandBox is null || SkinHelpEasyFieldsGrid is null)
        {
            return;
        }

        var values = _skinHelpEasyFields.ToDictionary(field => field.Index, field => field.Value);
        var command = NormalizeSkinHelpCommand(ReadSkinHelpEasyCommand());
        var fields = _skinHelpTemplatesByCommand.TryGetValue(command, out var template)
            ? CsvUtil.Split(template.RawLine)
            : new List<string> { command };
        LoadSkinHelpEasyFields(command, fields, values);
        TryBuildSkinHelpEasyCsv(out _);
    }

    private void LoadSkinHelpEasyFields(string command, IReadOnlyList<string> sourceFields, IReadOnlyDictionary<int, string>? preserveValues)
    {
        _skinHelpEasyFields.Clear();
        var templateFields = ReadSkinHelpTemplateFields(command, sourceFields);
        var maxIndex = Math.Max(sourceFields.Count, templateFields.Count) - 1;
        if (preserveValues is not null && preserveValues.Count > 0)
        {
            maxIndex = Math.Max(maxIndex, preserveValues.Keys.Max());
        }

        for (var index = 1; index <= maxIndex; index++)
        {
            var templateName = ReadSkinHelpFieldName(templateFields, index);
            var value = preserveValues is not null && preserveValues.TryGetValue(index, out var preserved)
                ? preserved
                : ReadCsvField(sourceFields, index);
            _skinHelpEasyFields.Add(new SkinHelpFieldEdit
            {
                Index = index,
                Name = FormatSkinHelpEasyFieldName(templateName, index),
                Value = value,
                Hint = FormatSkinHelpEasyFieldHint(templateName, index)
            });
        }

        SkinHelpEasyFieldsGrid.ItemsSource = null;
        SkinHelpEasyFieldsGrid.ItemsSource = _skinHelpEasyFields;
    }

    private IReadOnlyList<string> ReadSkinHelpTemplateFields(string command, IReadOnlyList<string> fallbackFields)
    {
        var normalized = NormalizeSkinHelpCommand(command);
        return _skinHelpTemplatesByCommand.TryGetValue(normalized, out var template)
            ? CsvUtil.Split(template.RawLine)
            : fallbackFields;
    }

    private bool TryBuildSkinHelpEasyCsv(out string csv)
    {
        csv = string.Empty;
        var command = NormalizeSkinHelpCommand(ReadSkinHelpEasyCommand());
        if (string.IsNullOrWhiteSpace(command) || command == "#")
        {
            SetStatus("Choose a helper command first.");
            return false;
        }

        var fields = new List<string> { command };
        fields.AddRange(_skinHelpEasyFields
            .OrderBy(field => field.Index)
            .Select(field => field.Value.Trim()));
        csv = CsvUtil.Join(fields);
        SkinHelpEasyCsvBox.Text = csv;
        return true;
    }

    private string ReadSkinHelpEasyCommand()
    {
        return SkinHelpEasyCommandBox?.Text.Trim() ?? string.Empty;
    }

    private void SetSkinHelpEasyButtonsEnabled(bool enabled)
    {
        SkinHelpEasyApplyButton.IsEnabled = enabled;
        SkinHelpEasyInsertButton.IsEnabled = enabled;
        SkinHelpEasyResetButton.IsEnabled = enabled;
    }

    private static string FormatSkinHelpEasyFieldName(string templateName, int index)
    {
        var name = string.IsNullOrWhiteSpace(templateName) ? $"field{index}" : templateName.Trim();
        var friendly = ReadSkinHelpEasyKoreanName(name);
        return string.IsNullOrWhiteSpace(friendly) ? name : $"{name} - {friendly}";
    }

    private static string FormatSkinHelpEasyFieldHint(string templateName, int index)
    {
        var name = string.IsNullOrWhiteSpace(templateName) ? $"field{index}" : templateName.Trim();
        var friendly = ReadSkinHelpEasyKoreanName(name);
        return string.IsNullOrWhiteSpace(friendly)
            ? $"Field {index}. Keep the old value if unsure."
            : $"{friendly}. Keep the old value if unsure.";
    }

    private static string ReadSkinHelpEasyKoreanName(string templateName)
    {
        var key = templateName.Trim().TrimStart('$').Trim('(', ')').ToLowerInvariant();
        return key switch
        {
            "x" => "X position",
            "y" => "Y position",
            "w" => "width",
            "h" => "height",
            "time" => "time",
            "gr" => "image slot",
            "font" => "font slot",
            "a" => "opacity",
            "r" => "red",
            "g" => "green",
            "b" => "blue",
            "blend" => "blend mode",
            "filter" => "filter",
            "angle" => "rotation",
            "center" => "rotation center",
            "loop" => "loop",
            "timer" => "timer",
            "cycle" => "animation cycle",
            "div_x" => "horizontal pieces",
            "div_y" => "vertical pieces",
            "align" => "alignment",
            "keta" => "digits",
            "title" => "title",
            "maker" => "author",
            "path" => "file path",
            "default" => "default value",
            "type" => "type",
            "thumbnail" => "thumbnail",
            "acc" => "movement curve",
            "size" => "text size",
            "edit" => "editable flag",
            "panel" => "panel",
            "range" => "range",
            "disable" => "disabled value",
            "muki" => "direction",
            "null" => "usually blank",
            _ when key.StartsWith("op", StringComparison.OrdinalIgnoreCase) => "condition option",
            _ => string.Empty
        };
    }

    private void ApplySkinHelpEntryEditIfChanged(SkinHelpEntry entry, string editedColumn, SkinHelpEditSnapshot? editSnapshot)
    {
        if (editSnapshot is not null && editSnapshot.Matches(entry, editedColumn) && editSnapshot.HasSameValues(entry))
        {
            return;
        }

        ApplySkinHelpEntryEdit(entry, editedColumn);
    }

    private void ApplySkinHelpEntryEdit(SkinHelpEntry entry, string editedColumn)
    {
        if (_applyingSkinHelpEdit || !entry.CanEdit)
        {
            return;
        }

        _applyingSkinHelpEdit = true;
        try
        {
            if (!TryBuildSkinHelpCsv(entry, editedColumn, out var csv))
            {
                ApplySkinHelpFilter();
                return;
            }
            if (entry.IsTemplate)
            {
                ApplySkinHelpTemplateEdit(entry, csv);
            }
            else
            {
                ApplySkinHelpDocumentEdit(entry, csv);
            }
        }
        finally
        {
            _applyingSkinHelpEdit = false;
        }
    }
    private bool TryBuildSkinHelpCsv(SkinHelpEntry entry, string editedColumn, out string csv)
    {
        csv = editedColumn.Equals("CSV", StringComparison.OrdinalIgnoreCase)
            ? entry.RawLine.Trim()
            : BuildSkinHelpCsv(entry.Command, entry.Arguments);

        if (string.IsNullOrWhiteSpace(csv))
        {
            SetStatus("Helper CSV is empty.");
            return false;
        }

        if (csv.Contains('\r') || csv.Contains('\n'))
        {
            SetStatus("Helper CSV must be a single line.");
            return false;
        }

        var fields = CsvUtil.Split(csv);
        if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0]) || !fields[0].Trim().StartsWith('#'))
        {
            SetStatus("Helper command must start with #.");
            return false;
        }

        return true;
    }

    private static string BuildSkinHelpCsv(string command, string arguments)
    {
        var fields = new List<string> { NormalizeSkinHelpCommand(command) };
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            fields.AddRange(CsvUtil.Split(arguments).Select(field => field.Trim()));
        }

        return CsvUtil.Join(fields);
    }

    private static string NormalizeSkinHelpCommand(string command)
    {
        var normalized = command.Trim();
        if (normalized.Length == 0) return normalized;
        return normalized.StartsWith('#') ? normalized : "#" + normalized;
    }

    private void ApplySkinHelpTemplateEdit(SkinHelpEntry entry, string csv)
    {
        try
        {
            var text = File.ReadAllText(entry.SourcePath, Encoding.UTF8);
            var updatedText = Lr2SkinParser.ReplaceLine(text, entry.SourceLineNumber, csv);
            File.WriteAllText(entry.SourcePath, updatedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LoadSkinHelp();
            RefreshSkinHelpRows();
            SetStatus($"Updated skinHelper.txt line {entry.SourceLineNumber:N0}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Helper template edit failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplySkinHelpDocumentEdit(SkinHelpEntry entry, string csv)
    {
        if (_document is null)
        {
            SetStatus("Open a .lr2skin file before editing current skin rows.");
            return;
        }

        try
        {
            if (!ReplacePreviewCodeDocumentLine(new PreviewCsvLineUpdate(entry.SourcePath, entry.SourceLineNumber, csv)))
            {
                return;
            }

            _dirty = true;
            UpdateTitle();
            RefreshDocumentFromEditor(_selectedPreviewObjectId);
            RenderPreview();
            SetStatus($"Updated {IOPath.GetFileName(entry.SourcePath)}:{entry.SourceLineNumber:N0}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Helper row edit failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectCurrentSkinHelpMode()
    {
        if (SkinHelpModeBox is not null && SkinHelpModeBox.SelectedIndex != 0)
        {
            SkinHelpModeBox.SelectedIndex = 0;
        }
    }

    private void RefreshSkinIfRows()
    {
        _skinIfRows.Clear();
        if (SkinIfTree is null || SkinIfSummaryText is null)
        {
            return;
        }

        SkinIfTree.ItemsSource = null;
        if (_document is null)
        {
            SkinIfSummaryText.Text = "Open a .lr2skin file to inspect IF blocks.";
            SkinIfExpander.IsEnabled = false;
            return;
        }

        var stack = new Stack<SkinIfBlockView>();
        var commandCount = 0;
        foreach (var line in _document.Lines.Where(line => line.Fields.Count > 0))
        {
            if (!IsSkinIfCommand(line.Command))
            {
                continue;
            }

            commandCount++;
            var node = CreateSkinIfNode(line);
            if (line.Command.Equals("#IF", StringComparison.OrdinalIgnoreCase))
            {
                AddSkinIfNode(stack, node);
                stack.Push(node);
                continue;
            }

            if (stack.Count == 0)
            {
                _skinIfRows.Add(node);
                continue;
            }

            stack.Peek().Children.Add(node);
            if (line.Command.Equals("#ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                stack.Pop();
            }
        }

        while (stack.Count > 0)
        {
            var unclosed = stack.Pop();
            unclosed.Children.Add(new SkinIfBlockView
            {
                Header = "missing #ENDIF",
                Summary = "block is not closed",
                Detail = $"{unclosed.Header} has no matching #ENDIF.",
                SourcePath = unclosed.SourcePath,
                SourceLineNumber = unclosed.SourceLineNumber
            });
        }

        SkinIfTree.ItemsSource = _skinIfRows;
        SkinIfSummaryText.Text = commandCount == 0
            ? "No IF/ELSEIF/ELSE/ENDIF rows in the current skin."
            : $"{_skinIfRows.Count:N0} root block(s), {commandCount:N0} IF-family row(s).";
        SkinIfExpander.IsEnabled = commandCount > 0;
    }

    private void AddSkinIfNode(Stack<SkinIfBlockView> stack, SkinIfBlockView node)
    {
        if (stack.Count > 0)
        {
            stack.Peek().Children.Add(node);
        }
        else
        {
            // Root nodes stay as TreeView top-level items; nested IFs remain collapsible inside their parent block.
            _skinIfRows.Add(node);
        }
    }

    private SkinIfBlockView CreateSkinIfNode(SkinCommandLine line)
    {
        var arguments = FormatSkinHelpArguments(line.Fields);
        var source = IOPath.GetFileName(line.SourcePath);
        if (!line.IsMainFile)
        {
            source += " (include)";
        }

        return new SkinIfBlockView
        {
            Header = $"{source}:{line.SourceLine} {line.Command}",
            Summary = arguments,
            Detail = FormatSkinIfDetail(line, arguments),
            SourcePath = line.SourcePath,
            SourceLineNumber = line.SourceLine
        };
    }

    private string FormatSkinIfDetail(SkinCommandLine line, string arguments)
    {
        _skinHelpTemplatesByCommand.TryGetValue(line.Command, out var template);
        return FormatSkinHelpDetail(line, template, arguments, string.Empty);
    }

    private static bool IsSkinIfCommand(string command)
    {
        return command.Equals("#IF", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("#ELSEIF", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("#ELSE", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("#ENDIF", StringComparison.OrdinalIgnoreCase);
    }

    private string ReadSkinHelpGroup(string command)
    {
        return _skinHelpGroupByCommand.TryGetValue(command, out var group) ? group : string.Empty;
    }

    private void LoadSkinObjectGroups(string path)
    {
        var currentGroup = string.Empty;
        var sortOrder = 0;
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('$'))
            {
                currentGroup = line;
                continue;
            }

            if (!line.StartsWith('#'))
            {
                continue;
            }

            if (!_skinHelpSortOrderByCommand.ContainsKey(line))
            {
                _skinHelpSortOrderByCommand[line] = sortOrder++;
            }

            if (currentGroup.Length > 0)
            {
                _skinHelpGroupByCommand[line] = currentGroup;
            }
        }
    }

    private string ReadSkinHelpMode()
    {
        return SkinHelpModeBox is not null && SkinHelpModeBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : "current";
    }

    private static string FormatSkinHelpPosition(SkinCommandLine line)
    {
        var command = line.Command;
        if (line.Fields.Count <= 0) return string.Empty;

        if (command.StartsWith("#SRC_", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("#DST_", StringComparison.OrdinalIgnoreCase))
        {
            return FormatCsvRect(line.Fields, xIndex: 3, yIndex: 4, widthIndex: 5, heightIndex: 6);
        }

        if (command.Equals("#RESOLUTION", StringComparison.OrdinalIgnoreCase) && line.Fields.Count >= 3)
        {
            return $"w={ReadCsvField(line.Fields, 1)}, h={ReadCsvField(line.Fields, 2)}";
        }

        return string.Empty;
    }

    private static string FormatCsvRect(IReadOnlyList<string> fields, int xIndex, int yIndex, int widthIndex, int heightIndex)
    {
        if (fields.Count <= heightIndex) return string.Empty;
        return $"x={ReadCsvField(fields, xIndex)}, y={ReadCsvField(fields, yIndex)}, w={ReadCsvField(fields, widthIndex)}, h={ReadCsvField(fields, heightIndex)}";
    }

    private static string ReadCsvField(IReadOnlyList<string> fields, int index)
    {
        return index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;
    }

    private static string FormatSkinHelpArguments(IReadOnlyList<string> fields)
    {
        if (fields.Count <= 1) return string.Empty;
        return CsvUtil.Join(fields.Skip(1).Select(field => field.Trim()).ToList());
    }

    private static string FormatSkinHelpTemplateDetail(string path, int lineNumber, IReadOnlyList<string> fields, string rawLine)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{path}:{lineNumber}");
        builder.AppendLine(rawLine);
        builder.AppendLine();

        if (fields.Count <= 1)
        {
            builder.AppendLine("No arguments.");
            return builder.ToString().TrimEnd();
        }

        for (var i = 1; i < fields.Count; i++)
        {
            builder.AppendLine($"{i}: {ReadSkinHelpFieldName(fields, i)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatSkinHelpDetail(SkinCommandLine line, SkinHelpEntry? template, string arguments, string position)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{line.SourcePath}:{line.SourceLine}");
        builder.AppendLine(line.RawText.Trim());
        if (!string.IsNullOrWhiteSpace(position))
        {
            builder.AppendLine($"CSV position: {position}");
        }
        builder.AppendLine();

        if (line.Fields.Count <= 1)
        {
            builder.AppendLine("No arguments.");
            return builder.ToString().TrimEnd();
        }

        IReadOnlyList<string> templateFields = template is null ? Array.Empty<string>() : CsvUtil.Split(template.RawLine);
        for (var i = 1; i < line.Fields.Count; i++)
        {
            builder.AppendLine($"{i}: {ReadSkinHelpFieldName(templateFields, i)} = {line.Fields[i].Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            builder.AppendLine();
            builder.AppendLine(arguments);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ReadSkinHelpFieldName(IReadOnlyList<string> templateFields, int index)
    {
        if (index >= 0 && index < templateFields.Count && !string.IsNullOrWhiteSpace(templateFields[index]))
        {
            return templateFields[index].Trim();
        }

        return $"field{index}";
    }

    private static string? FindSkinObjGroupPath()
    {
        return FindSkinSupportFile("skinObjGroup.txt");
    }
    private static string? FindSkinHelperPath()
    {
        return FindSkinSupportFile("skinHelper.txt");
    }

    private static string? FindSkinSupportFile(string fileName)
    {
        var candidates = new[]
        {
            IOPath.Combine(AppContext.BaseDirectory, fileName),
            IOPath.Combine(Environment.CurrentDirectory, fileName),
            IOPath.Combine(Environment.CurrentDirectory, "SkinEditorNext", fileName),
            IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var fullPath = IOPath.GetFullPath(candidate);
            if (!seen.Add(fullPath)) continue;
            if (File.Exists(fullPath)) return fullPath;
        }

        return null;
    }
}
