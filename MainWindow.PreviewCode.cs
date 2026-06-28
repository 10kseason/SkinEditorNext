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
    private void PreviewCodeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewCodeTabs.SelectedItem is TabItem { Tag: PreviewCodeDocument document })
        {
            UpdatePreviewCodeStatus(document);
        }
    }

    private void ReloadPreviewCodeTabs_Click(object sender, RoutedEventArgs e)
    {
        BuildPreviewCodeTabs();
        SetStatus("Reloaded preview code tabs.");
    }

    private void PreviewCodeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingPreviewCodeEditors || sender is not TextBox editor || editor.Tag is not PreviewCodeDocument document)
        {
            return;
        }

        document.Text = editor.Text;
        document.IsDirty = true;
        _dirty = true;

        if (document.IsMain && CodeEditor.Text != document.Text)
        {
            _loadingEditor = true;
            CodeEditor.Text = document.Text;
            _loadingEditor = false;
        }

        UpdatePreviewCodeTabHeader(document);
        UpdatePreviewCodeStatus(document);
        SchedulePreviewCodeRefresh();
        UpdateTitle();
        if (!_applyingPreviewCodeCompletion)
        {
            UpdatePreviewCodeCompletion(editor);
        }
    }


    private void PreviewCodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox editor)
        {
            return;
        }

        if (IsPreviewCodeCompletionOpenFor(editor))
        {
            if (e.Key == Key.Down)
            {
                MovePreviewCodeCompletionSelection(1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                MovePreviewCodeCompletionSelection(-1);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Enter or Key.Tab)
            {
                CommitPreviewCodeCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClosePreviewCodeCompletion();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            UpdatePreviewCodeCompletion(editor, force: true);
            e.Handled = true;
        }
    }

    private void PreviewCodeEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ClosePreviewCodeCompletion();
    }

    private void UpdatePreviewCodeCompletion(TextBox editor, bool force = false)
    {
        if (!TryReadPreviewCodeCompletionContext(editor, force, out var context))
        {
            ClosePreviewCodeCompletion();
            return;
        }

        var completions = ReadPreviewCodeCommandCompletions(context.Prefix).ToList();
        if (completions.Count == 0 || completions.Any(completion => completion.Command.Equals(context.Prefix, StringComparison.OrdinalIgnoreCase)))
        {
            ClosePreviewCodeCompletion();
            return;
        }

        EnsurePreviewCodeCompletionPopup();
        if (_previewCodeCompletionPopup is null || _previewCodeCompletionList is null)
        {
            return;
        }

        _previewCodeCompletionEditor = editor;
        _previewCodeCompletionStart = context.Start;
        _previewCodeCompletionLength = context.Length;
        _previewCodeCompletionList.ItemsSource = completions;
        _previewCodeCompletionList.SelectedIndex = 0;

        var rect = ReadCaretRect(editor);
        _previewCodeCompletionPopup.PlacementTarget = editor;
        _previewCodeCompletionPopup.Placement = PlacementMode.Relative;
        _previewCodeCompletionPopup.HorizontalOffset = Math.Max(0, rect.Left);
        _previewCodeCompletionPopup.VerticalOffset = Math.Max(0, rect.Bottom + 2);
        _previewCodeCompletionPopup.IsOpen = true;
    }

    private IEnumerable<PreviewCommandCompletion> ReadPreviewCodeCommandCompletions(string prefix)
    {
        var commands = _skinHelpCommandChoices.Count > 0
            ? _skinHelpCommandChoices
            : new List<string> { "#SRC_IMAGE", "#DST_IMAGE", "#SRC_TEXT", "#DST_TEXT" };
        return commands
            .Where(command => command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(command => command, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .Select(command => new PreviewCommandCompletion(command, ReadSkinHelpGroup(command)));
    }

    private static bool TryReadPreviewCodeCompletionContext(TextBox editor, bool force, out PreviewCodeCompletionContext context)
    {
        context = default;
        var caret = editor.CaretIndex;
        var text = editor.Text;
        if (caret < 0 || caret > text.Length)
        {
            return false;
        }

        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var beforeCaret = text.Substring(lineStart, caret - lineStart);
        var commandStartInLine = beforeCaret.Length - beforeCaret.TrimStart().Length;
        if (commandStartInLine >= beforeCaret.Length || beforeCaret[commandStartInLine] != '#')
        {
            return false;
        }

        var prefix = beforeCaret[commandStartInLine..];
        if (prefix.Length < 2 && !force)
        {
            return false;
        }

        if (prefix.Any(character => char.IsWhiteSpace(character) || character == ','))
        {
            return false;
        }

        var start = lineStart + commandStartInLine;
        context = new PreviewCodeCompletionContext(start, caret - start, prefix);
        return true;
    }

    private static Rect ReadCaretRect(TextBox editor)
    {
        try
        {
            var rect = editor.GetRectFromCharacterIndex(editor.CaretIndex, trailingEdge: true);
            if (!rect.IsEmpty)
            {
                return rect;
            }
        }
        catch
        {
        }

        return new Rect(8, 20, 0, 16);
    }

    private void EnsurePreviewCodeCompletionPopup()
    {
        if (_previewCodeCompletionPopup is not null)
        {
            return;
        }

        _previewCodeCompletionList = new ListBox
        {
            DisplayMemberPath = nameof(PreviewCommandCompletion.Label),
            MinWidth = 260,
            MaxHeight = 128,
            FontFamily = new FontFamily("Consolas, Yu Gothic UI, Meiryo, MS Gothic"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
            BorderThickness = new Thickness(0),
            Focusable = false
        };
        _previewCodeCompletionList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindVisualAncestor<ListBoxItem>(source) is { DataContext: PreviewCommandCompletion completion })
            {
                _previewCodeCompletionList.SelectedItem = completion;
                CommitPreviewCodeCompletion();
                e.Handled = true;
            }
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(34, 211, 238)),
            BorderThickness = new Thickness(1),
            Child = _previewCodeCompletionList
        };

        _previewCodeCompletionPopup = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Child = border
        };
    }

    private bool IsPreviewCodeCompletionOpenFor(TextBox editor)
    {
        return _previewCodeCompletionPopup?.IsOpen == true && ReferenceEquals(_previewCodeCompletionEditor, editor);
    }

    private void MovePreviewCodeCompletionSelection(int delta)
    {
        if (_previewCodeCompletionList is null || _previewCodeCompletionList.Items.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(_previewCodeCompletionList.SelectedIndex + delta, 0, _previewCodeCompletionList.Items.Count - 1);
        _previewCodeCompletionList.SelectedIndex = next;
        _previewCodeCompletionList.ScrollIntoView(_previewCodeCompletionList.SelectedItem);
    }

    private void CommitPreviewCodeCompletion()
    {
        if (_previewCodeCompletionEditor is null || _previewCodeCompletionList?.SelectedItem is not PreviewCommandCompletion completion)
        {
            ClosePreviewCodeCompletion();
            return;
        }

        var editor = _previewCodeCompletionEditor;
        var text = editor.Text;
        if (_previewCodeCompletionStart < 0 || _previewCodeCompletionStart + _previewCodeCompletionLength > text.Length)
        {
            ClosePreviewCodeCompletion();
            return;
        }

        _applyingPreviewCodeCompletion = true;
        editor.Text = text.Remove(_previewCodeCompletionStart, _previewCodeCompletionLength)
            .Insert(_previewCodeCompletionStart, completion.Command);
        editor.CaretIndex = _previewCodeCompletionStart + completion.Command.Length;
        _applyingPreviewCodeCompletion = false;
        ClosePreviewCodeCompletion();
        editor.Focus();
    }

    private void ClosePreviewCodeCompletion()
    {
        if (_previewCodeCompletionPopup is not null)
        {
            _previewCodeCompletionPopup.IsOpen = false;
        }

        _previewCodeCompletionEditor = null;
        _previewCodeCompletionStart = 0;
        _previewCodeCompletionLength = 0;
    }
    private void PreviewCodeRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _previewCodeRefreshTimer.Stop();
        RefreshDocumentFromPreviewEditors();
    }

    private void SchedulePreviewCodeRefresh()
    {
        if (_document is null) return;
        _previewCodeRefreshTimer.Stop();
        _previewCodeRefreshTimer.Start();
    }

    private void RefreshDocumentFromPreviewEditors()
    {
        if (_document is null) return;

        try
        {
            var selectedObjectId = _selectedPreviewObjectId;
            var path = _document.MainPath;
            var encoding = _document.Encoding;
            _document = _parser.ParseMainText(path, CodeEditor.Text, encoding, ReadPreviewSourceTextOverrides());
            var rebuildTabs = !PreviewCodeSourceSetMatchesDocument();
            UpdateDocumentViews(selectedObjectId, rebuildTabs);
            RenderPreview();
            UpdatePreviewCodeStatus();
        }
        catch (Exception ex)
        {
            if (PreviewCodeStatusText is not null)
            {
                PreviewCodeStatusText.Text = "parse failed";
            }

            DiagnosticsBox.Text = ex.Message;
        }
    }

    private void BuildPreviewCodeTabs()
    {
        if (PreviewCodeTabs is null)
        {
            return;
        }

        _previewCodeRefreshTimer.Stop();
        var selectedPath = PreviewCodeTabs.SelectedItem is TabItem { Tag: PreviewCodeDocument selectedDocument }
            ? selectedDocument.Path
            : string.Empty;
        var existingByPath = _previewCodeDocuments
            .GroupBy(document => NormalizeComparablePath(document.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var specs = ReadPreviewCodeDocumentSpecs();
        _loadingPreviewCodeEditors = true;
        _previewCodeDocuments.Clear();
        PreviewCodeTabs.Items.Clear();

        foreach (var spec in specs)
        {
            var document = CreatePreviewCodeDocument(spec, existingByPath);
            _previewCodeDocuments.Add(document);
            PreviewCodeTabs.Items.Add(CreatePreviewCodeTab(document));
        }

        _loadingPreviewCodeEditors = false;

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            var normalizedSelected = NormalizeComparablePath(selectedPath);
            for (var i = 0; i < _previewCodeDocuments.Count; i++)
            {
                if (IsSamePath(_previewCodeDocuments[i].Path, normalizedSelected))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        if (PreviewCodeTabs.Items.Count > 0)
        {
            PreviewCodeTabs.SelectedIndex = Math.Clamp(selectedIndex, 0, PreviewCodeTabs.Items.Count - 1);
        }

        UpdatePreviewCodeStatus();
    }

    private IReadOnlyList<PreviewCodeDocumentSpec> ReadPreviewCodeDocumentSpecs()
    {
        if (_document is null)
        {
            return Array.Empty<PreviewCodeDocumentSpec>();
        }

        var specs = new List<PreviewCodeDocumentSpec>
        {
            new(_document.MainPath, IsMain: true)
        };

        foreach (var group in _document.Lines
                     .Where(line => !line.IsMainFile && !string.IsNullOrWhiteSpace(line.SourcePath))
                     .GroupBy(line => NormalizeComparablePath(line.SourcePath), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Min(line => line.DisplayLine)))
        {
            specs.Add(new(group.First().SourcePath, IsMain: false));
        }

        return specs;
    }

    private PreviewCodeDocument CreatePreviewCodeDocument(
        PreviewCodeDocumentSpec spec,
        IReadOnlyDictionary<string, PreviewCodeDocument> existingByPath)
    {
        var normalizedPath = NormalizeComparablePath(spec.Path);
        if (existingByPath.TryGetValue(normalizedPath, out var existing))
        {
            return new PreviewCodeDocument
            {
                Path = spec.Path,
                Encoding = spec.IsMain
                    ? _document?.Encoding ?? existing.Encoding
                    : existing.Encoding,
                IsMain = spec.IsMain,
                Text = ReadPreviewCodeDocumentText(existing),
                IsDirty = existing.IsDirty
            };
        }

        if (spec.IsMain)
        {
            return new PreviewCodeDocument
            {
                Path = spec.Path,
                Encoding = _document?.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                IsMain = true,
                Text = CodeEditor.Text
            };
        }

        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var text = string.Empty;
        if (File.Exists(spec.Path))
        {
            var bytes = File.ReadAllBytes(spec.Path);
            encoding = Lr2SkinParser.DetectEncoding(bytes);
            text = encoding.GetString(bytes);
        }

        return new PreviewCodeDocument
        {
            Path = spec.Path,
            Encoding = encoding,
            IsMain = false,
            Text = text
        };
    }

    private TabItem CreatePreviewCodeTab(PreviewCodeDocument document)
    {
        var editor = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            Text = document.Text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Yu Gothic UI, Meiryo, MS Gothic"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)),
            Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Tag = document
        };
        editor.TextChanged += PreviewCodeEditor_TextChanged;
        editor.PreviewKeyDown += PreviewCodeEditor_PreviewKeyDown;
        editor.LostKeyboardFocus += PreviewCodeEditor_LostKeyboardFocus;
        document.Editor = editor;

        return new TabItem
        {
            Header = CreatePreviewCodeTabHeader(document),
            Content = editor,
            Tag = document
        };
    }

    private bool PreviewCodeSourceSetMatchesDocument()
    {
        var specs = ReadPreviewCodeDocumentSpecs();
        if (specs.Count != _previewCodeDocuments.Count)
        {
            return false;
        }

        for (var i = 0; i < specs.Count; i++)
        {
            if (!IsSamePath(specs[i].Path, _previewCodeDocuments[i].Path))
            {
                return false;
            }
        }

        return true;
    }

    private Dictionary<string, string> ReadPreviewSourceTextOverrides()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in _previewCodeDocuments.Where(document => !document.IsMain))
        {
            overrides[NormalizeComparablePath(document.Path)] = ReadPreviewCodeDocumentText(document);
        }

        return overrides;
    }

    private string ReadMainEditorText()
    {
        var mainDocument = _previewCodeDocuments.FirstOrDefault(document => document.IsMain);
        return mainDocument is null ? CodeEditor.Text : ReadPreviewCodeDocumentText(mainDocument);
    }

    private static string ReadPreviewCodeDocumentText(PreviewCodeDocument document)
    {
        return document.Editor?.Text ?? document.Text;
    }

    private void SyncPreviewMainDocumentFromCodeEditor()
    {
        var mainDocument = _previewCodeDocuments.FirstOrDefault(document => document.IsMain);
        if (mainDocument is null)
        {
            return;
        }

        mainDocument.Text = CodeEditor.Text;
        mainDocument.IsDirty = true;
        if (mainDocument.Editor is not null && mainDocument.Editor.Text != CodeEditor.Text)
        {
            _loadingPreviewCodeEditors = true;
            mainDocument.Editor.Text = CodeEditor.Text;
            _loadingPreviewCodeEditors = false;
        }

        UpdatePreviewCodeTabHeader(mainDocument);
        UpdatePreviewCodeStatus(mainDocument);
    }

    private bool ReplacePreviewCodeDocumentLine(PreviewCsvLineUpdate update)
    {
        var document = FindPreviewCodeDocument(update.Path);
        if (document is null)
        {
            SetStatus($"Preview code tab not found: {update.Path}");
            return false;
        }

        var updatedText = Lr2SkinParser.ReplaceLine(ReadPreviewCodeDocumentText(document), update.LineNumber, update.Csv);
        document.Text = updatedText;
        document.IsDirty = true;

        if (document.IsMain)
        {
            SetEditorText(updatedText, markDirty: true);
        }

        if (document.Editor is not null && document.Editor.Text != updatedText)
        {
            _loadingPreviewCodeEditors = true;
            document.Editor.Text = updatedText;
            _loadingPreviewCodeEditors = false;
        }

        UpdatePreviewCodeTabHeader(document);
        UpdatePreviewCodeStatus(document);
        return true;
    }

    private PreviewCodeDocument? FindPreviewCodeDocument(string path)
    {
        return _previewCodeDocuments.FirstOrDefault(document => IsSamePath(document.Path, path));
    }

    private TextBlock CreatePreviewCodeTabHeader(PreviewCodeDocument document)
    {
        return new TextBlock { Text = FormatPreviewCodeTabHeader(document) };
    }

    private void UpdatePreviewCodeTabHeader(PreviewCodeDocument document)
    {
        if (PreviewCodeTabs is null) return;

        foreach (TabItem item in PreviewCodeTabs.Items)
        {
            if (ReferenceEquals(item.Tag, document))
            {
                if (item.Header is TextBlock header)
                {
                    header.Text = FormatPreviewCodeTabHeader(document);
                }
                else
                {
                    item.Header = CreatePreviewCodeTabHeader(document);
                }

                return;
            }
        }
    }

    private void UpdatePreviewCodeStatus(PreviewCodeDocument? selectedDocument = null)
    {
        if (PreviewCodeStatusText is null) return;

        if (_document is null)
        {
            PreviewCodeStatusText.Text = string.Empty;
            return;
        }

        selectedDocument ??= PreviewCodeTabs?.SelectedItem is TabItem { Tag: PreviewCodeDocument document }
            ? document
            : _previewCodeDocuments.FirstOrDefault();
        var csvCount = _previewCodeDocuments.Count(document => document.IsCsv);
        var dirtyCount = _previewCodeDocuments.Count(document => document.IsDirty);
        var selected = selectedDocument is null
            ? string.Empty
            : $" / {FormatPreviewCodeTabKind(selectedDocument)} / {selectedDocument.Encoding.WebName}";
        var dirty = dirtyCount == 0 ? string.Empty : $" / {dirtyCount:N0} dirty";
        PreviewCodeStatusText.Text = $"{ReadSkinTypeLabel(_document.Header.Type)} / {csvCount:N0} csv{selected}{dirty}";
    }

    private string FormatPreviewCodeTabHeader(PreviewCodeDocument document)
    {
        var dirty = document.IsDirty ? "*" : string.Empty;
        return $"{dirty}{FormatPreviewCodeTabKind(document)} {FormatPreviewCodePathLabel(document.Path)}";
    }

    private static string FormatPreviewCodeTabKind(PreviewCodeDocument document)
    {
        if (document.IsMain) return "Main";
        return document.IsCsv ? "CSV" : "Include";
    }

    private string FormatPreviewCodePathLabel(string path)
    {
        if (_document is not null && !string.IsNullOrWhiteSpace(_document.MainPath))
        {
            var directory = IOPath.GetDirectoryName(_document.MainPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                try
                {
                    var relative = IOPath.GetRelativePath(directory, path);
                    if (!relative.StartsWith("..", StringComparison.Ordinal) && !IOPath.IsPathRooted(relative))
                    {
                        return relative.Replace('\\', '/');
                    }
                }
                catch
                {
                }
            }
        }

        return IOPath.GetFileName(path);
    }

    private static string ReadSkinTypeLabel(int type)
    {
        return type switch
        {
            0 => "7KEYS",
            1 => "5KEYS",
            2 => "14KEYS",
            3 => "10KEYS",
            4 => "9KEYS",
            5 => "SELECT",
            6 => "DECIDE",
            7 => "RESULT",
            8 => "KEYCONFIG",
            9 => "SKINSELECT",
            10 => "SOUNDSET",
            11 => "THEME",
            12 => "7KEYSBATTLE",
            13 => "5KEYSBATTLE",
            14 => "9KEYSBATTLE",
            15 => "COURSERESULT",
            16 => "OPENING",
            17 => "MODESELECT",
            18 => "MODEDECIDE",
            19 => "COURSESELECT",
            20 => "COURSEEDIT",
            _ => $"TYPE {type}"
        };
    }
}
