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

public partial class MainWindow : Window
{
    private const double PreviewZoomStep = 1.1;
    private const double PreviewMinZoom = 0.25;
    private const double PreviewMaxZoom = 8.0;
    private readonly Lr2SkinParser _parser = new();
    private readonly Lr2PreviewEvaluator _previewEvaluator = new();
    private readonly Lr2BitmapFactory _bitmapFactory = new();
    private readonly List<SkinHelpEntry> _skinHelpTemplates = new();
    private readonly Dictionary<string, SkinHelpEntry> _skinHelpTemplatesByCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _skinHelpGroupByCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _skinHelpSortOrderByCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkinHelpEntry> _skinHelpRows = new();
    private readonly List<string> _skinHelpCommandChoices = new();
    private readonly List<SkinHelpFieldEdit> _skinHelpEasyFields = new();
    private readonly List<SkinIfBlockView> _skinIfRows = new();
    private readonly List<SkinImportEntry> _skinImportEntries = new();
    private readonly List<Lr2PreviewItem> _previewItems = new();
    private readonly List<PreviewCodeDocument> _previewCodeDocuments = new();
    private readonly DispatcherTimer _previewCodeRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Popup? _previewCodeCompletionPopup;
    private ListBox? _previewCodeCompletionList;
    private TextBox? _previewCodeCompletionEditor;
    private int _previewCodeCompletionStart;
    private int _previewCodeCompletionLength;
    private bool _applyingPreviewCodeCompletion;
    private Point? _previewContextPoint;
    private Lr2SkinDocument? _document;
    private bool _loadingEditor;
    private bool _loadingPreviewCodeEditors;
    private bool _syncingPreviewTime;
    private bool _dirty;
    private int? _selectedPreviewObjectId;
    private Lr2PreviewItem? _selectedPreviewItem;
    private FrameworkElement? _selectedPreviewVisual;
    private Rectangle? _selectedPreviewAdorner;
    private bool _previewEditMode;
    private bool _draggingPreviewObject;
    private Point _previewDragStartPoint;
    private int _previewDragStartX;
    private int _previewDragStartY;
    private int _previewDragLastDx;
    private int _previewDragLastDy;
    private double _previewZoom = 1.0;
    private double _previewFitScale = 1.0;
    private bool _panningPreview;
    private bool _previewPanMoved;
    private Point _previewPanStartPoint;
    private Point _previewPanStartCanvasPoint;
    private double _previewPanStartHorizontalOffset;
    private double _previewPanStartVerticalOffset;
    private bool _applyingSkinHelpEdit;
    private SkinHelpEditSnapshot? _skinHelpEditSnapshot;
    private bool _loadingSkinHelpEasyEditor;

    public IReadOnlyList<string> SkinHelpCommandChoices => _skinHelpCommandChoices;

    public MainWindow()
    {
        InitializeComponent();
        _previewCodeRefreshTimer.Tick += PreviewCodeRefreshTimer_Tick;
        LoadSkinHelp();
        RefreshLr2ThemeImports();
        SetEmptyState();
    }

    private void NewSkin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewSkinDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        var saveDialog = new SaveFileDialog
        {
            Filter = "LR2 skin files (*.lr2skin)|*.lr2skin|All files (*.*)|*.*",
            Title = "Create LR2 skin",
            FileName = MakeSafeFileName(dialog.Settings.Title) + ".lr2skin"
        };

        if (saveDialog.ShowDialog(this) != true) return;

        try
        {
            Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var text = Lr2SkinWriter.CreateNewSkin(dialog.Settings);
            File.WriteAllText(saveDialog.FileName, text, encoding);
            _bitmapFactory.Clear();
            _document = _parser.ParseMainText(saveDialog.FileName, text, encoding);
            SetEditorText(text, markDirty: false);
            ResetPreviewZoom();
            SelectCurrentSkinHelpMode();
            UpdateDocumentViews();
            RenderPreview();
            SetStatus($"Created {IOPath.GetFileName(saveDialog.FileName)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "New skin failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "LR2 skin files (*.lr2skin)|*.lr2skin|All files (*.*)|*.*",
            Title = "Open LR2 skin"
        };

        if (dialog.ShowDialog(this) != true) return;
        OpenDocument(dialog.FileName);
    }

    private void ImportSkin_Click(object sender, RoutedEventArgs e)
    {
        if (Lr2ImportSkinBox.SelectedItem is not SkinImportEntry entry)
        {
            SetStatus("Select an LR2 Theme skin first.");
            return;
        }

        if (!OpenDocument(entry.FullPath)) return;

        if (TryReadImportResolutionPreset(out var width, out var height, out var presetName))
        {
            SetEditorText(Lr2SkinParser.ApplyResolution(CodeEditor.Text, width, height), markDirty: true);
            RefreshDocumentFromEditor();
            RenderPreview();
            SetStatus($"Imported {entry.DisplayName}; applied {presetName} #RESOLUTION,{width},{height}. Save to write the change.");
            return;
        }

        SetStatus($"Imported {entry.DisplayName}.");
    }

    private void ApplyImportResolution_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadImportResolutionPreset(out var width, out var height, out var presetName))
        {
            SetStatus("Select SD, HD, FHD, or 4K before applying.");
            return;
        }

        SetEditorText(Lr2SkinParser.ApplyResolution(CodeEditor.Text, width, height), markDirty: true);
        RefreshDocumentFromEditor();
        RenderPreview();
        SetStatus($"Applied {presetName} #RESOLUTION,{width},{height}. Save to write the change.");
    }

    private void RefreshLr2Import_Click(object sender, RoutedEventArgs e)
    {
        RefreshLr2ThemeImports();
        SetStatus(_skinImportEntries.Count == 0
            ? "LR2files\\Theme not found or no .lr2skin files were found."
            : $"Found {_skinImportEntries.Count:N0} LR2 Theme skin(s).");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_document.MainPath))
        {
            SaveAs_Click(sender, e);
            return;
        }

        SaveDocument(_document.MainPath);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "LR2 skin files (*.lr2skin)|*.lr2skin|All files (*.*)|*.*",
            Title = "Save LR2 skin",
            FileName = _document is null ? "new.lr2skin" : IOPath.GetFileName(_document.MainPath)
        };

        if (dialog.ShowDialog(this) != true) return;
        SaveDocument(dialog.FileName);
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        RefreshDocumentFromEditor();
        SetStatus("Validated current code.");
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        RefreshDocumentFromEditor();
        RenderPreview();
        SetStatus("Preview refreshed.");
    }

    private void ApplyResolution_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPositiveInt(ResolutionWidthBox, "resolution width", out var width) ||
            !TryReadPositiveInt(ResolutionHeightBox, "resolution height", out var height))
        {
            return;
        }

        SetEditorText(Lr2SkinParser.ApplyResolution(CodeEditor.Text, width, height), markDirty: true);
        RefreshDocumentFromEditor();
        RenderPreview();
        SetStatus($"Applied #RESOLUTION,{width},{height}.");
    }

    private void RefreshLr2ThemeImports()
    {
        if (Lr2ImportSkinBox is null || Lr2ImportSummaryText is null || ImportSkinButton is null || ApplyImportResolutionButton is null || ImportResolutionPresetBox is null) return;

        _skinImportEntries.Clear();
        Lr2ImportSkinBox.ItemsSource = null;

        var themeDirectory = FindLr2ThemeDirectory();
        if (themeDirectory is null)
        {
            Lr2ImportSummaryText.Text = "not found";
            Lr2ImportSkinBox.IsEnabled = false;
            ImportSkinButton.IsEnabled = false;
            ImportResolutionPresetBox.IsEnabled = true;
            ApplyImportResolutionButton.IsEnabled = true;
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(themeDirectory, "*.lr2skin", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = IOPath.GetRelativePath(themeDirectory, path);
                _skinImportEntries.Add(new SkinImportEntry(
                    path,
                    relativePath,
                    relativePath.Replace('\\', '/')));
            }
        }
        catch (Exception ex)
        {
            Lr2ImportSummaryText.Text = $"scan failed: {ex.Message}";
            Lr2ImportSkinBox.IsEnabled = false;
            ImportSkinButton.IsEnabled = false;
            ImportResolutionPresetBox.IsEnabled = true;
            ApplyImportResolutionButton.IsEnabled = true;
            return;
        }

        Lr2ImportSkinBox.ItemsSource = _skinImportEntries;
        Lr2ImportSkinBox.IsEnabled = _skinImportEntries.Count > 0;
        ImportSkinButton.IsEnabled = _skinImportEntries.Count > 0;
        ImportResolutionPresetBox.IsEnabled = true;
        ApplyImportResolutionButton.IsEnabled = true;
        if (_skinImportEntries.Count > 0)
        {
            Lr2ImportSkinBox.SelectedIndex = 0;
        }

        Lr2ImportSummaryText.Text = _skinImportEntries.Count == 0
            ? "0 skin(s)"
            : $"{_skinImportEntries.Count:N0} skin(s)";
    }

    private bool TryReadImportResolutionPreset(out int width, out int height, out string presetName)
    {
        width = 0;
        height = 0;
        presetName = string.Empty;

        if (ImportResolutionPresetBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return false;
        }

        if (tag.Equals("keep", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = tag.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        presetName = item.Content?.ToString() ?? $"{width}x{height}";
        return true;
    }

    private static string? FindLr2ThemeDirectory()
    {
        foreach (var root in EnumerateLr2RootCandidates())
        {
            var themeDirectory = IOPath.Combine(root, "LR2files", "Theme");
            if (Directory.Exists(themeDirectory))
            {
                return themeDirectory;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateLr2RootCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(seed)) continue;

            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(IOPath.GetFullPath(seed));
            }
            catch
            {
                continue;
            }

            for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }
            }
        }
    }
    private void ApplyObject_Click(object sender, RoutedEventArgs e)
    {
        if (ObjectsGrid.SelectedItem is not SkinObjectView item)
        {
            SetStatus("No object selected.");
            return;
        }


        if (!TryReadInt(SourceXBox, "source x", out var sx) ||
            !TryReadInt(SourceYBox, "source y", out var sy) ||
            !TryReadInt(SourceWidthBox, "source width", out var sw) ||
            !TryReadInt(SourceHeightBox, "source height", out var sh) ||
            !TryReadInt(DestXBox, "dest x", out var dx) ||
            !TryReadInt(DestYBox, "dest y", out var dy) ||
            !TryReadInt(DestWidthBox, "dest width", out var dw) ||
            !TryReadInt(DestHeightBox, "dest height", out var dh))
        {
            return;
        }

        item.SourceX = sx;
        item.SourceY = sy;
        item.SourceWidth = sw;
        item.SourceHeight = sh;
        item.DestX = dx;
        item.DestY = dy;
        item.DestWidth = dw;
        item.DestHeight = dh;

        if (!ApplyPreviewObjectGeometry(item))
        {
            return;
        }

        RefreshDocumentFromEditor(item.Id);
        RenderPreview();
        SetStatus($"Applied object geometry for #{item.Id} ({(item.IsEditableInMain ? "main" : "include")}).");
    }

    private void ApplyPreviewCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || _selectedPreviewObjectId is null)
        {
            SetStatus("Select a preview object first.");
            return;
        }

        var item = _document.Objects.FirstOrDefault(candidate => candidate.Id == _selectedPreviewObjectId.Value);
        if (item is null)
        {
            SetStatus("Selected preview object is no longer available.");
            return;
        }

        if (!TryReadPreviewCsvLine(PreviewSourceCsvBox, "#SRC_", "SRC CSV", out var sourceCsv))
        {
            return;
        }

        string? destinationCsv = null;
        if (item.DstLine > 0 && !TryReadPreviewCsvLine(PreviewDestinationCsvBox, "#DST_", "DST CSV", out destinationCsv))
        {
            return;
        }

        if (!ApplyPreviewCsvLines(item, sourceCsv, destinationCsv))
        {
            return;
        }

        RefreshDocumentFromEditor(item.Id);
        RenderPreview();
        SetStatus($"Applied CSV for preview object #{item.Id}.");
    }

    private void ResetPreviewCsv_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreviewSelectionPanel();
        SetStatus("Reset preview CSV editor.");
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDocumentDirectory(out var directory)) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.bmp;*.tga)|*.png;*.bmp;*.tga|All files (*.*)|*.*",
            Title = "Add LR2 image asset",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true) return;

        var lines = new List<string>();
        try
        {
            // LR2 loads #IMAGE paths from its process/root path in practice. Store copied assets
            // under the skin folder, but emit LR2files\... paths when the skin lives inside LR2files.
            // Existing names are never overwritten; GetUniqueAssetPath adds _1, _2, ...
            var assetDirectory = IOPath.Combine(directory, "assets");
            Directory.CreateDirectory(assetDirectory);

            foreach (var sourcePath in dialog.FileNames)
            {
                var extension = IOPath.GetExtension(sourcePath);
                if (!IsSupportedImageExtension(extension))
                {
                    SetStatus($"Unsupported image extension: {extension}");
                    continue;
                }

                var destinationPath = GetUniqueAssetPath(assetDirectory, IOPath.GetFileName(sourcePath));
                File.Copy(sourcePath, destinationPath, overwrite: false);
                var relativePath = Lr2SkinWriter.ToLr2LoadPath(destinationPath, directory);
                lines.Add(Lr2SkinWriter.ImageLine(relativePath));
            }

            if (lines.Count == 0) return;
            AppendGeneratedLines(lines, "image asset");
            _bitmapFactory.Clear();
            RefreshDocumentFromEditor();
            ImageAssetBox.SelectedItem = _document?.ImageSlots.LastOrDefault();
            UpdateAssetPreview();
            RenderPreview();
            SetStatus($"Added {lines.Count} image asset(s).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add image failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImageAssetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAssetPreview();
    }

    private void UpdateAssetPreview()
    {
        if (AssetPreviewImage is null || AssetPreviewLabel is null || AssetPreviewInfoText is null) return;

        AssetPreviewImage.Source = null;
        AssetPreviewLabel.Visibility = Visibility.Visible;

        if (_document is null)
        {
            AssetPreviewLabel.Text = "Open a skin first";
            AssetPreviewInfoText.Text = string.Empty;
            return;
        }

        if (ImageAssetBox.SelectedItem is not SkinImageSlot slot)
        {
            AssetPreviewLabel.Text = "No asset selected";
            AssetPreviewInfoText.Text = _document.ImageSlots.Count == 0 ? "No #IMAGE rows in current skin." : string.Empty;
            return;
        }

        var info = new StringBuilder();
        info.Append($"#{slot.Index} {IOPath.GetFileName(slot.RawPath.Length > 0 ? slot.RawPath : slot.ResolvedPath)}");
        if (Lr2ImageProbe.TryGetSize(slot.ResolvedPath, out var width, out var height))
        {
            info.Append($" / {width}x{height}");
        }

        info.AppendLine();
        info.AppendLine($"line: {IOPath.GetFileName(slot.SourceFile)}:{slot.SourceLine}");
        info.Append($"path: {slot.RawPath}");
        AssetPreviewInfoText.Text = info.ToString();

        if (_bitmapFactory.TryLoadImage(slot.ResolvedPath, out var bitmap))
        {
            AssetPreviewImage.Source = bitmap;
            AssetPreviewLabel.Visibility = Visibility.Collapsed;
            return;
        }

        AssetPreviewLabel.Text = "Image unavailable";
    }

    private void SelectImageAssetForObject(SkinObjectView item)
    {
        if (_document is null || item.SourceGraph < 0) return;

        var slot = _document.ImageSlots.FirstOrDefault(candidate => candidate.Index == item.SourceGraph);
        if (slot is null) return;

        if (!ReferenceEquals(ImageAssetBox.SelectedItem, slot))
        {
            ImageAssetBox.SelectedItem = slot;
        }
        else
        {
            UpdateAssetPreview();
        }
    }

    private void AddObject_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            SetStatus("Create or open a .lr2skin file first.");
            return;
        }

        if (!TryReadDstSpec(out var dst)) return;

        var kind = ReadCreateKind();
        var lines = new List<string>();
        var selectNewestObject = true;

        if (string.Equals(kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            // Text can work without an image asset. If the skin has no font slot yet, create a simple #FONT.
            if (!TryReadInt(CreateValueBox, "text kind", out var textKind)) return;
            var fontSlot = _document.FontSlots.FirstOrDefault()?.Index ?? _document.FontSlots.Count;
            if (_document.FontSlots.Count == 0)
            {
                lines.Add(Lr2SkinWriter.FontLine());
            }

            lines.AddRange(Lr2SkinWriter.TextObjectLines(fontSlot, textKind, align: 1, dst));
        }
        else
        {
            if (!TryGetSelectedImageSlot(out var imageSlot)) return;
            var sourceWidth = dst.Width;
            var sourceHeight = dst.Height;
            // For image/number objects, default SRC size to the actual asset size when it can be read.
            if (Lr2ImageProbe.TryGetSize(imageSlot.ResolvedPath, out var imageWidth, out var imageHeight))
            {
                sourceWidth = imageWidth;
                sourceHeight = imageHeight;
            }

            if (string.Equals(kind, "number", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadInt(CreateValueBox, "number op1", out var numberOp)) return;
                lines.AddRange(Lr2SkinWriter.NumberObjectLines(imageSlot.Index, sourceWidth, sourceHeight, numberOp, dst));
            }
            else
            {
                lines.AddRange(Lr2SkinWriter.ImageObjectLines(imageSlot.Index, sourceWidth, sourceHeight, dst));
            }
        }

        AppendGeneratedLines(lines, $"{kind} object");
        RefreshDocumentFromEditor();
        if (selectNewestObject)
        {
            ObjectsGrid.SelectedItem = _document?.Objects.LastOrDefault();
        }
        RenderPreview();
        SetStatus($"Added {kind} object.");
    }


    private void CreateObjectFromContext_Click(object sender, RoutedEventArgs e)
    {
        AddObject_Click(sender, e);
    }

    private void CreateObjectFromHelpContext_Click(object sender, RoutedEventArgs e)
    {
        if (SkinHelpGrid.SelectedItem is SkinHelpEntry entry)
        {
            SelectCreateKindFromCommand(entry.Command);
        }

        AddObject_Click(sender, e);
    }

    private void CreateObjectAtPreviewContext_Click(object sender, RoutedEventArgs e)
    {
        if (_previewContextPoint is Point point)
        {
            SetCreatePosition(point);
        }

        AddObject_Click(sender, e);
    }

    private void DeleteSelectedObject_Click(object sender, RoutedEventArgs e)
    {
        var item = ReadSelectedSkinObject();
        if (item is null)
        {
            SetStatus("Select an object first.");
            return;
        }

        DeletePreviewObject(item);
    }

    private void DeleteObjectFromHelpContext_Click(object sender, RoutedEventArgs e)
    {
        if (SkinHelpGrid.SelectedItem is not SkinHelpEntry entry)
        {
            SetStatus("Right-click a helper object row first.");
            return;
        }

        var item = FindObjectForSkinHelpEntry(entry);
        if (item is null)
        {
            SetStatus("This helper row is not a preview object row.");
            return;
        }

        DeletePreviewObject(item);
    }

    private void SelectCreateKindFromCommand(string command)
    {
        var tag = command.Contains("TEXT", StringComparison.OrdinalIgnoreCase)
            ? "text"
            : command.Contains("NUMBER", StringComparison.OrdinalIgnoreCase)
                ? "number"
                : command.Contains("IMAGE", StringComparison.OrdinalIgnoreCase)
                    ? "image"
                    : null;
        if (tag is null || CreateKindBox is null)
        {
            return;
        }

        foreach (ComboBoxItem item in CreateKindBox.Items)
        {
            if (item.Tag is string itemTag && itemTag.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                CreateKindBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SetCreatePosition(Point point)
    {
        CreateXBox.Text = Math.Max(0, (int)Math.Round(point.X)).ToString(CultureInfo.InvariantCulture);
        CreateYBox.Text = Math.Max(0, (int)Math.Round(point.Y)).ToString(CultureInfo.InvariantCulture);
    }

    private SkinObjectView? ReadSelectedSkinObject()
    {
        if (_document is null)
        {
            return null;
        }

        if (ObjectsGrid.SelectedItem is SkinObjectView selectedGridItem)
        {
            return selectedGridItem;
        }

        return _selectedPreviewObjectId is int selectedId
            ? _document.Objects.FirstOrDefault(item => item.Id == selectedId)
            : null;
    }

    private SkinObjectView? FindObjectForSkinHelpEntry(SkinHelpEntry entry)
    {
        if (_document is null || entry.IsTemplate)
        {
            return null;
        }

        if (entry.Command.StartsWith("#SRC_", StringComparison.OrdinalIgnoreCase))
        {
            return _document.Objects.FirstOrDefault(item =>
                item.SrcLine == entry.SourceLineNumber && IsSamePath(item.SourceFile, entry.SourcePath));
        }

        if (entry.Command.StartsWith("#DST_", StringComparison.OrdinalIgnoreCase))
        {
            return _document.Objects.FirstOrDefault(item =>
                IsSamePath(ReadObjectDstFile(item), entry.SourcePath) &&
                item.Frames.Any(frame => frame.Line == entry.SourceLineNumber));
        }

        return null;
    }
    private void ObjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ObjectsGrid.SelectedItem is not SkinObjectView item)
        {
            SelectedObjectText.Text = string.Empty;
            return;
        }

        _selectedPreviewObjectId = item.Id;
        SelectedObjectText.Text = $"{item.Kind} at {item.Location}" + (item.IsEditableInMain ? string.Empty : " (include)");
        SelectImageAssetForObject(item);
        SourceXBox.Text = item.SourceX.ToString(CultureInfo.InvariantCulture);
        SourceYBox.Text = item.SourceY.ToString(CultureInfo.InvariantCulture);
        SourceWidthBox.Text = item.SourceWidth.ToString(CultureInfo.InvariantCulture);
        SourceHeightBox.Text = item.SourceHeight.ToString(CultureInfo.InvariantCulture);
        DestXBox.Text = item.DestX.ToString(CultureInfo.InvariantCulture);
        DestYBox.Text = item.DestY.ToString(CultureInfo.InvariantCulture);
        DestWidthBox.Text = item.DestWidth.ToString(CultureInfo.InvariantCulture);
        DestHeightBox.Text = item.DestHeight.ToString(CultureInfo.InvariantCulture);
    }

    private void DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var row = FindVisualAncestor<DataGridRow>(source);
        if (row?.Item is null)
        {
            return;
        }

        grid.SelectedItem = row.Item;
        row.Focus();
    }

    private void PreviewTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingPreviewTime || !IsLoaded) return;
        _syncingPreviewTime = true;
        PreviewTimeBox.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
        _syncingPreviewTime = false;
        RenderPreview();
    }

    private void PreviewTimeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!TryReadPreviewTime(out var timeMs))
        {
            PreviewTimeBox.Text = ((int)PreviewTimeSlider.Value).ToString(CultureInfo.InvariantCulture);
            return;
        }

        _syncingPreviewTime = true;
        PreviewTimeSlider.Value = timeMs;
        PreviewTimeBox.Text = ((int)timeMs).ToString(CultureInfo.InvariantCulture);
        _syncingPreviewTime = false;
        RenderPreview();
    }

    private void PreviewModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RenderPreview();
    }

    private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingEditor) return;
        _dirty = true;
        SyncPreviewMainDocumentFromCodeEditor();
        SchedulePreviewCodeRefresh();
        UpdateTitle();
        if (!_applyingPreviewCodeCompletion)
        {
            UpdatePreviewCodeCompletion(CodeEditor);
        }
    }

    private void SkinHelpSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySkinHelpFilter(preservePosition: false);
    }

    private void SkinHelpModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySkinHelpFilter(preservePosition: false);
    }

    private void SkinHelpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = SkinHelpGrid.SelectedItem as SkinHelpEntry;
        SkinHelpDetailBox.Text = entry?.Detail ?? string.Empty;
        LoadSkinHelpEasyEditor(entry);
    }

    private void SkinHelpGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        var editedColumn = e.Column.Header?.ToString() ?? string.Empty;
        if (e.Row.Item is not SkinHelpEntry entry || !entry.CanEdit || !IsEditableSkinHelpColumn(editedColumn))
        {
            _skinHelpEditSnapshot = null;
            e.Cancel = true;
            return;
        }

        _skinHelpEditSnapshot = SkinHelpEditSnapshot.Capture(entry, editedColumn);
    }

    private void SkinHelpGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not SkinHelpEntry entry)
        {
            _skinHelpEditSnapshot = null;
            return;
        }

        var editedColumn = e.Column.Header?.ToString() ?? string.Empty;
        var editSnapshot = _skinHelpEditSnapshot;
        _skinHelpEditSnapshot = null;
        Dispatcher.BeginInvoke(new Action(() => ApplySkinHelpEntryEditIfChanged(entry, editedColumn, editSnapshot)), DispatcherPriority.Background);
    }

    private static bool IsEditableSkinHelpColumn(string? header)
    {
        return header is "Command" or "Fields" or "CSV";
    }

    private void SkinIfTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SkinIfBlockView node)
        {
            SkinHelpDetailBox.Text = node.Detail;
        }
    }

    private void SkinHelpEasyCommandBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingSkinHelpEasyEditor)
        {
            RefreshSkinHelpEasyFieldsForCommand();
        }
    }

    private void SkinHelpEasyCommandBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_loadingSkinHelpEasyEditor)
        {
            RefreshSkinHelpEasyFieldsForCommand();
        }
    }

    private void SkinHelpEasyApply_Click(object sender, RoutedEventArgs e)
    {
        if (SkinHelpGrid.SelectedItem is not SkinHelpEntry entry)
        {
            SetStatus("Select a helper row first.");
            return;
        }

        if (!TryBuildSkinHelpEasyCsv(out var csv))
        {
            return;
        }

        entry.Command = NormalizeSkinHelpCommand(ReadSkinHelpEasyCommand());
        entry.Arguments = FormatSkinHelpArguments(CsvUtil.Split(csv));
        entry.RawLine = csv;
        ApplySkinHelpEntryEdit(entry, "CSV");
    }

    private void SkinHelpEasyInsert_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildSkinHelpEasyCsv(out var csv))
        {
            return;
        }

        CodeEditor.Focus();
        var insertion = csv + Environment.NewLine;
        var insertionStart = CodeEditor.SelectionStart;
        CodeEditor.SelectedText = insertion;
        CodeEditor.CaretIndex = insertionStart + insertion.Length;
        SetStatus($"Inserted {NormalizeSkinHelpCommand(ReadSkinHelpEasyCommand())} from easy helper.");
    }

    private void SkinHelpEasyReset_Click(object sender, RoutedEventArgs e)
    {
        LoadSkinHelpEasyEditor(SkinHelpGrid.SelectedItem as SkinHelpEntry);
    }

    private void InsertSkinHelp_Click(object sender, RoutedEventArgs e)
    {
        InsertSelectedSkinHelpLine();
    }

    private bool OpenDocument(string path)
    {
        try
        {
            _previewCodeRefreshTimer.Stop();
            _previewCodeDocuments.Clear();
            _bitmapFactory.Clear();
            _document = _parser.Load(path);
            SetEditorText(_document.MainText, markDirty: false);
            ResetPreviewZoom();
            SelectCurrentSkinHelpMode();
            UpdateDocumentViews();
            RenderPreview();
            SetStatus($"Opened {IOPath.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void SaveDocument(string path)
    {
        try
        {
            _previewCodeRefreshTimer.Stop();
            var encoding = _document?.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var mainText = ReadMainEditorText();
            File.WriteAllText(path, mainText, encoding);

            var savedIncludes = 0;
            foreach (var document in _previewCodeDocuments.Where(document => !document.IsMain && document.IsDirty))
            {
                File.WriteAllText(document.Path, ReadPreviewCodeDocumentText(document), document.Encoding);
                document.IsDirty = false;
                savedIncludes++;
            }

            if (CodeEditor.Text != mainText)
            {
                SetEditorText(mainText, markDirty: false);
            }

            foreach (var document in _previewCodeDocuments)
            {
                if (document.IsMain)
                {
                    document.Path = path;
                }

                document.IsDirty = false;
                UpdatePreviewCodeTabHeader(document);
            }

            _dirty = false;
            _document = _parser.ParseMainText(path, mainText, encoding, ReadPreviewSourceTextOverrides());
            UpdateDocumentViews();
            RenderPreview();
            SetStatus(savedIncludes == 0
                ? $"Saved {IOPath.GetFileName(path)}."
                : $"Saved {IOPath.GetFileName(path)} and {savedIncludes:N0} include file(s).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshDocumentFromEditor(int? selectedObjectId = null, bool rebuildPreviewCodeTabs = true)
    {
        var path = _document?.MainPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = IOPath.Combine(Environment.CurrentDirectory, "untitled.lr2skin");
        }

        var encoding = _document?.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _document = _parser.ParseMainText(path, CodeEditor.Text, encoding, ReadPreviewSourceTextOverrides());
        UpdateDocumentViews(selectedObjectId, rebuildPreviewCodeTabs);
    }

    private void UpdateDocumentViews(int? selectedObjectId = null, bool rebuildPreviewCodeTabs = true)
    {
        if (_document is null)
        {
            SetEmptyState();
            return;
        }

        FilePathText.Text = _document.MainPath;
        EncodingText.Text = $"Encoding: {_document.Encoding.WebName}";
        ResolutionWidthBox.Text = _document.Resolution.Width.ToString(CultureInfo.InvariantCulture);
        ResolutionHeightBox.Text = _document.Resolution.Height.ToString(CultureInfo.InvariantCulture);
        DiagnosticsBox.Text = _document.Diagnostics.Count == 0
            ? "No diagnostics."
            : string.Join(Environment.NewLine, _document.Diagnostics);

        AssetsSummaryText.Text = $"{_document.ImageSlots.Count:N0} image(s)";
        ImageAssetBox.ItemsSource = null;
        ImageAssetBox.ItemsSource = _document.ImageSlots;
        if (_document.ImageSlots.Count > 0 && ImageAssetBox.SelectedItem is null)
        {
            ImageAssetBox.SelectedIndex = 0;
        }
        UpdateAssetPreview();

        ObjectsGrid.ItemsSource = null;
        ObjectsGrid.ItemsSource = _document.Objects;

        if (selectedObjectId is not null)
        {
            ObjectsGrid.SelectedItem = _document.Objects.FirstOrDefault(item => item.Id == selectedObjectId.Value);
        }

        FooterText.Text = $"{_document.Lines.Count:N0} parsed lines, {_document.Objects.Count:N0} objects, resolution {_document.Resolution}";
        RefreshSkinHelpRows();
        RefreshSkinIfRows();
        if (rebuildPreviewCodeTabs || !PreviewCodeSourceSetMatchesDocument())
        {
            BuildPreviewCodeTabs();
        }
        else
        {
            UpdatePreviewCodeStatus();
        }
        UpdateTitle();
    }

    private void SetEditorText(string text, bool markDirty)
    {
        _loadingEditor = true;
        CodeEditor.Text = text;
        _loadingEditor = false;
        _dirty = markDirty;
        UpdateTitle();
    }

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
    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdatePreviewZoomTransform();
    }

    private void PreviewScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChangePreviewZoom(e.Delta > 0 ? PreviewZoomStep : 1.0 / PreviewZoomStep, e.GetPosition(PreviewScrollViewer));
        PreviewCanvas.Focus();
        e.Handled = true;
    }

    private bool TryHandlePreviewZoomKey(Key key)
    {
        switch (key)
        {
            case Key.OemPlus:
            case Key.Add:
                ChangePreviewZoom(PreviewZoomStep, null);
                return true;
            case Key.OemMinus:
            case Key.Subtract:
                ChangePreviewZoom(1.0 / PreviewZoomStep, null);
                return true;
            default:
                return false;
        }
    }

    private void ChangePreviewZoom(double factor, Point? viewportPoint)
    {
        if (_document is null || factor <= 0)
        {
            return;
        }

        var oldScale = ReadPreviewActualScale();
        var oldHorizontalOffset = PreviewScrollViewer.HorizontalOffset;
        var oldVerticalOffset = PreviewScrollViewer.VerticalOffset;
        var point = viewportPoint ?? ReadPreviewViewportCenter();

        _previewZoom = Math.Clamp(_previewZoom * factor, PreviewMinZoom, PreviewMaxZoom);
        UpdatePreviewZoomTransform();
        var newScale = ReadPreviewActualScale();

        Dispatcher.BeginInvoke(() =>
        {
            if (oldScale <= 0) return;

            var scaleRatio = newScale / oldScale;
            PreviewScrollViewer.ScrollToHorizontalOffset((oldHorizontalOffset + point.X) * scaleRatio - point.X);
            PreviewScrollViewer.ScrollToVerticalOffset((oldVerticalOffset + point.Y) * scaleRatio - point.Y);
        }, DispatcherPriority.Loaded);

        SetStatus($"Preview zoom {FormatPreviewZoomLabel()}.");
    }

    private Point ReadPreviewViewportCenter()
    {
        var width = PreviewScrollViewer.ViewportWidth > 0 ? PreviewScrollViewer.ViewportWidth : PreviewScrollViewer.ActualWidth;
        var height = PreviewScrollViewer.ViewportHeight > 0 ? PreviewScrollViewer.ViewportHeight : PreviewScrollViewer.ActualHeight;
        return new Point(Math.Max(0, width / 2.0), Math.Max(0, height / 2.0));
    }

    private void ResetPreviewZoom()
    {
        _previewZoom = 1.0;
        UpdatePreviewZoomTransform();
    }

    private void UpdatePreviewZoomTransform()
    {
        if (PreviewCanvasScale is null || PreviewScrollViewer is null || PreviewCanvas is null)
        {
            return;
        }

        _previewFitScale = CalculatePreviewFitScale();
        var scale = ReadPreviewActualScale();
        PreviewCanvasScale.ScaleX = scale;
        PreviewCanvasScale.ScaleY = scale;
        UpdatePreviewZoomText();
    }

    private double CalculatePreviewFitScale()
    {
        if (PreviewCanvas.Width <= 0 || PreviewCanvas.Height <= 0)
        {
            return 1.0;
        }

        var viewportWidth = PreviewScrollViewer.ViewportWidth > 0 ? PreviewScrollViewer.ViewportWidth : PreviewScrollViewer.ActualWidth;
        var viewportHeight = PreviewScrollViewer.ViewportHeight > 0 ? PreviewScrollViewer.ViewportHeight : PreviewScrollViewer.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return 1.0;
        }

        var scale = Math.Min(viewportWidth / PreviewCanvas.Width, viewportHeight / PreviewCanvas.Height);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1.0;
        }

        return Math.Min(1.0, scale);
    }

    private double ReadPreviewActualScale()
    {
        return Math.Max(0.01, _previewFitScale * _previewZoom);
    }

    private string FormatPreviewZoomLabel()
    {
        return $"Zoom {Math.Round(_previewZoom * 100):0}%";
    }

    private void UpdatePreviewZoomText()
    {
        if (PreviewZoomText is not null)
        {
            PreviewZoomText.Text = FormatPreviewZoomLabel();
        }
    }
    private void RenderPreview()
    {
        PreviewCanvas.Children.Clear();
        _previewItems.Clear();
        _selectedPreviewItem = null;
        _selectedPreviewVisual = null;
        _selectedPreviewAdorner = null;

        if (_document is null)
        {
            PreviewCanvas.Width = 640;
            PreviewCanvas.Height = 480;
            PreviewOverlay.Text = "Open a .lr2skin file.";
            ResetPreviewZoom();
            UpdatePreviewZoomTransform();
            ClearPreviewDrag();
            UpdatePreviewSelectionPanel();
            return;
        }

        var resolution = _document.Resolution.IsValid ? _document.Resolution : ResolutionInfo.Default;
        var clock = ReadPreviewClock();
        var renderItems = _previewEvaluator.Evaluate(_document, clock, maxItems: 3000);
        _previewItems.AddRange(renderItems);
        if (_selectedPreviewObjectId is not null)
        {
            _selectedPreviewItem = _previewItems.LastOrDefault(item => item.Object.Id == _selectedPreviewObjectId.Value);
        }

        PreviewCanvas.Width = resolution.Width;
        PreviewCanvas.Height = resolution.Height;
        PreviewOverlay.Text = $"{resolution.Width} x {resolution.Height} / {renderItems.Count:N0} visible / {_document.Objects.Count:N0} objects / {(int)clock.TimeMs} ms / {clock.ModeName} / {ReadPreviewEditModeLabel()} / {FormatPreviewZoomLabel()}";
        UpdatePreviewZoomTransform();

        AddPreviewSceneBitmap(renderItems, resolution, _selectedPreviewItem?.Object.Id);

        if (_selectedPreviewItem is not null)
        {
            AddSelectedPreviewVisual(_selectedPreviewItem);
            AddPreviewSelectionAdorner(_selectedPreviewItem);
        }

        UpdatePreviewSelectionPanel();
    }

    private void AddPreviewSceneBitmap(IReadOnlyList<Lr2PreviewItem> renderItems, ResolutionInfo resolution, int? excludedObjectId)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, resolution.Width, resolution.Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 24, 39)), null, bounds);

            foreach (var item in renderItems)
            {
                if (excludedObjectId is not null && item.Object.Id == excludedObjectId.Value) continue;
                DrawPreviewItem(dc, item);
            }

            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(229, 231, 235)), 1), bounds);
        }

        var bitmap = new RenderTargetBitmap(
            resolution.Width,
            resolution.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Width = resolution.Width,
            Height = resolution.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        PreviewCanvas.Children.Add(image);
    }

    private void AddSelectedPreviewVisual(Lr2PreviewItem item)
    {
        var visual = CreatePreviewObjectVisual(item);
        if (visual is null) return;

        var dest = item.Destination;
        _selectedPreviewVisual = visual;
        Canvas.SetLeft(visual, dest.X);
        Canvas.SetTop(visual, dest.Y);
        PreviewCanvas.Children.Add(visual);
    }

    private FrameworkElement? CreatePreviewObjectVisual(Lr2PreviewItem item)
    {
        var dest = item.Destination;
        if (dest.Width <= 0 || dest.Height <= 0) return null;

        FrameworkElement visual;
        if (item.Object.Kind.Equals("#SRC_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            visual = CreateTextPlaceholderVisual(item, new Rect(dest.X, dest.Y, dest.Width, dest.Height));
        }
        else if (_bitmapFactory.TryCreateFrameBitmap(item, out var frameBitmap))
        {
            visual = new Image
            {
                Source = frameBitmap,
                Width = dest.Width,
                Height = dest.Height,
                Stretch = Stretch.Fill,
                Opacity = item.Opacity,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            visual = CreateMissingPreviewObjectVisual(item, new Rect(dest.X, dest.Y, dest.Width, dest.Height));
        }

        visual.IsHitTestVisible = false;
        ApplyRotation(visual, item.Angle, item.Center);
        return visual;
    }

    private static Border CreateTextPlaceholderVisual(Lr2PreviewItem item, Rect dest)
    {
        var placeholderColor = Color.FromRgb((byte)item.Red, (byte)item.Green, (byte)item.Blue);
        var isGuide = item.Object.SourceIndex is >= 9000 and < 9100;
        return new Border
        {
            Width = dest.Width,
            Height = dest.Height,
            Opacity = item.Opacity,
            BorderBrush = new SolidColorBrush(placeholderColor),
            BorderThickness = new Thickness(isGuide ? 2 : 1),
            Background = new SolidColorBrush(Color.FromArgb(isGuide ? (byte)92 : (byte)32, placeholderColor.R, placeholderColor.G, placeholderColor.B)),
            Child = new TextBlock
            {
                Text = ReadTextPlaceholderLabel(item.Object),
                Foreground = new SolidColorBrush(ReadContrastingTextColor(placeholderColor)),
                FontFamily = new FontFamily("Consolas, Yu Gothic UI, Meiryo, MS Gothic"),
                FontWeight = isGuide ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = Math.Clamp(Math.Min(dest.Height * 0.18, dest.Width * 0.22), 9, 28),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Padding = new Thickness(4, 0, 4, 0)
            },
            IsHitTestVisible = false
        };
    }

    private static Rectangle CreateMissingPreviewObjectVisual(Lr2PreviewItem item, Rect dest)
    {
        return new Rectangle
        {
            Width = dest.Width,
            Height = dest.Height,
            Stroke = item.Object.Kind.Equals("#SRC_IMAGE", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                : new SolidColorBrush(Color.FromRgb(250, 204, 21)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            IsHitTestVisible = false
        };
    }

    private void DrawPreviewItem(DrawingContext dc, Lr2PreviewItem item)
    {
        var dest = new Rect(item.Destination.X, item.Destination.Y, item.Destination.Width, item.Destination.Height);
        if (dest.Width <= 0 || dest.Height <= 0) return;

        if (item.Object.Kind.Equals("#SRC_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            DrawTextPlaceholder(dc, item, dest);
            return;
        }

        if (_bitmapFactory.TryCreateFrameBitmap(item, out var frameBitmap))
        {
            PushPreviewRenderState(dc, item, dest);
            dc.DrawImage(frameBitmap, dest);
            PopPreviewRenderState(dc, item);
            return;
        }

        DrawMissingPreviewObject(dc, item, dest);
    }

    private void DrawTextPlaceholder(DrawingContext dc, Lr2PreviewItem item, Rect dest)
    {
        var placeholderColor = Color.FromRgb((byte)item.Red, (byte)item.Green, (byte)item.Blue);
        var borderBrush = new SolidColorBrush(placeholderColor);
        var isGuide = item.Object.SourceIndex is >= 9000 and < 9100;
        var background = new SolidColorBrush(Color.FromArgb(isGuide ? (byte)92 : (byte)32, placeholderColor.R, placeholderColor.G, placeholderColor.B));
        var label = ReadTextPlaceholderLabel(item.Object);
        var fontSize = Math.Clamp(Math.Min(dest.Height * 0.18, dest.Width * 0.22), 9, 28);
        var foreground = new SolidColorBrush(ReadContrastingTextColor(placeholderColor));
        var formattedText = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas, Yu Gothic UI, Meiryo, MS Gothic"), FontStyles.Normal, isGuide ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            fontSize,
            foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, dest.Width - 8),
            MaxTextHeight = Math.Max(1, dest.Height),
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis
        };

        PushPreviewRenderState(dc, item, dest);
        dc.DrawRectangle(background, new Pen(borderBrush, isGuide ? 2 : 1), dest);
        dc.DrawText(formattedText, new Point(dest.X + 4, dest.Y + Math.Max(0, (dest.Height - formattedText.Height) * 0.5)));
        PopPreviewRenderState(dc, item);
    }

    private static void DrawMissingPreviewObject(DrawingContext dc, Lr2PreviewItem item, Rect dest)
    {
        var strokeColor = item.Object.Kind.Equals("#SRC_IMAGE", StringComparison.OrdinalIgnoreCase)
            ? Color.FromRgb(56, 189, 248)
            : Color.FromRgb(250, 204, 21);
        var pen = new Pen(new SolidColorBrush(strokeColor), 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
        };

        PushPreviewRenderState(dc, item, dest);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), pen, dest);
        PopPreviewRenderState(dc, item);
    }

    private static void PushPreviewRenderState(DrawingContext dc, Lr2PreviewItem item, Rect dest)
    {
        if (Math.Abs(item.Opacity - 1.0) > 0.001)
        {
            dc.PushOpacity(item.Opacity);
        }

        if (Math.Abs(item.Angle) > 0.001)
        {
            var center = CenterToPoint(item.Center, dest);
            dc.PushTransform(new RotateTransform(item.Angle, center.X, center.Y));
        }
    }

    private static void PopPreviewRenderState(DrawingContext dc, Lr2PreviewItem item)
    {
        if (Math.Abs(item.Angle) > 0.001)
        {
            dc.Pop();
        }

        if (Math.Abs(item.Opacity - 1.0) > 0.001)
        {
            dc.Pop();
        }
    }

    private static Point CenterToPoint(int center, Rect dest)
    {
        return center switch
        {
            1 => new Point(dest.Left, dest.Bottom),
            2 => new Point(dest.Left + dest.Width * 0.5, dest.Bottom),
            3 => new Point(dest.Right, dest.Bottom),
            4 => new Point(dest.Left, dest.Top + dest.Height * 0.5),
            6 => new Point(dest.Right, dest.Top + dest.Height * 0.5),
            7 => new Point(dest.Left, dest.Top),
            8 => new Point(dest.Left + dest.Width * 0.5, dest.Top),
            9 => new Point(dest.Right, dest.Top),
            _ => new Point(dest.Left + dest.Width * 0.5, dest.Top + dest.Height * 0.5)
        };
    }

    private static string ReadTextPlaceholderLabel(SkinObjectView skinObject)
    {
        if (skinObject.SourceIndex is >= 9010 and <= 9016)
        {
            return $"LANE {skinObject.SourceIndex - 9009}";
        }

        if (skinObject.SourceIndex is >= 9020 and <= 9024)
        {
            return $"KEY {skinObject.SourceIndex - 9019}";
        }

        return skinObject.SourceIndex switch
        {
            9000 => "NOTE LINE",
            9001 => "KEY AREA",
            9002 => "GAUGE",
            9003 => "BGA AREA",
            _ => "TEXT"
        };
    }

    private static Color ReadContrastingTextColor(Color background)
    {
        var luminance = background.R * 0.299 + background.G * 0.587 + background.B * 0.114;
        return luminance > 160 ? Color.FromRgb(17, 24, 39) : Color.FromRgb(229, 231, 235);
    }

    private bool SelectPreviewItemAt(Point point, bool beginObjectDrag)
    {
        if (_document is null) return false;

        var item = FindPreviewItemAt(point);
        if (item is null)
        {
            ClearPreviewDrag();
            _selectedPreviewObjectId = null;
            _selectedPreviewItem = null;
            ObjectsGrid.SelectedItem = null;
            UpdatePreviewSelectionPanel();
            RenderPreview();
            SetStatus($"No preview object at {FormatDouble(point.X)},{FormatDouble(point.Y)}.");
            return false;
        }

        _selectedPreviewObjectId = item.Object.Id;
        _selectedPreviewItem = item;
        ObjectsGrid.SelectedItem = _document.Objects.FirstOrDefault(candidate => candidate.Id == item.Object.Id);
        if (beginObjectDrag)
        {
            BeginPreviewDrag(item, point);
        }

        RenderPreview();
        SetStatus($"Selected preview object #{item.Object.Id} {item.Object.Kind} at {FormatPreviewRect(item.Destination)}.");
        return true;
    }

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null) return;

        PreviewCanvas.Focus();
        if (!_previewEditMode)
        {
            BeginPreviewPan(e);
            e.Handled = true;
            return;
        }

        SelectPreviewItemAt(e.GetPosition(PreviewCanvas), beginObjectDrag: true);
        e.Handled = true;
    }


    private void PreviewCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        _previewContextPoint = e.GetPosition(PreviewCanvas);
        PreviewCanvas.Focus();
        SelectPreviewItemAt(_previewContextPoint.Value, beginObjectDrag: false);
    }
    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panningPreview)
        {
            UpdatePreviewPan(e);
            e.Handled = true;
            return;
        }

        if (!_draggingPreviewObject || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(PreviewCanvas);
        var dx = (int)Math.Round(point.X - _previewDragStartPoint.X);
        var dy = (int)Math.Round(point.Y - _previewDragStartPoint.Y);
        if (dx == _previewDragLastDx && dy == _previewDragLastDy)
        {
            return;
        }

        _previewDragLastDx = dx;
        _previewDragLastDy = dy;
        MoveSelectedPreviewVisualTo(_previewDragStartX + dx, _previewDragStartY + dy);
        e.Handled = true;
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panningPreview)
        {
            var shouldSelect = !_previewPanMoved;
            var selectPoint = _previewPanStartCanvasPoint;
            ClearPreviewPan();
            if (shouldSelect)
            {
                SelectPreviewItemAt(selectPoint, beginObjectDrag: false);
            }

            e.Handled = true;
            return;
        }

        if (!_draggingPreviewObject) return;

        var finalX = _previewDragStartX + _previewDragLastDx;
        var finalY = _previewDragStartY + _previewDragLastDy;
        var moved = _previewDragLastDx != 0 || _previewDragLastDy != 0;
        ClearPreviewDrag();
        if (moved)
        {
            MoveSelectedPreviewObjectTo(finalX, finalY);
        }

        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!PreviewTab.IsSelected || IsPreviewTextInputFocused())
        {
            return;
        }

        if (TryHandlePreviewZoomKey(e.Key))
        {
            PreviewCanvas.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.L)
        {
            return;
        }

        TogglePreviewEditMode();
        PreviewCanvas.Focus();
        e.Handled = true;
    }

    private static bool IsPreviewTextInputFocused()
    {
        return Keyboard.FocusedElement is TextBox or ComboBox;
    }
    private void PreviewCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (TryHandlePreviewZoomKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L)
        {
            TogglePreviewEditMode();
            e.Handled = true;
            return;
        }

        var dx = 0;
        var dy = 0;
        switch (e.Key)
        {
            case Key.Left:
                dx = -1;
                break;
            case Key.Right:
                dx = 1;
                break;
            case Key.Up:
                dy = -1;
                break;
            case Key.Down:
                dy = 1;
                break;
            default:
                return;
        }

        if (!_previewEditMode)
        {
            SetStatus("Preview is read-only. Press L to enable edit mode.");
            e.Handled = true;
            return;
        }

        if (MoveSelectedPreviewObjectBy(dx, dy))
        {
            e.Handled = true;
        }
    }
    private void TogglePreviewEditMode()
    {
        _previewEditMode = !_previewEditMode;
        ClearPreviewDrag();
        ClearPreviewPan();
        UpdatePreviewCursor();
        UpdatePreviewSelectionPanel();
        SetStatus(_previewEditMode
            ? "Preview edit mode enabled. Drag or use arrow keys to move by 1px. Press L for read-only."
            : "Preview read-only mode enabled. Press L for edit mode.");
    }

    private void MoveSelectedPreviewVisualTo(int x, int y)
    {
        if (_selectedPreviewItem is not null)
        {
            var dest = _selectedPreviewItem.Destination;
            _selectedPreviewItem = _selectedPreviewItem with { Destination = new PreviewRect(x, y, dest.Width, dest.Height) };
        }

        if (_selectedPreviewVisual is not null)
        {
            Canvas.SetLeft(_selectedPreviewVisual, x);
            Canvas.SetTop(_selectedPreviewVisual, y);
        }

        if (_selectedPreviewAdorner is not null)
        {
            Canvas.SetLeft(_selectedPreviewAdorner, x);
            Canvas.SetTop(_selectedPreviewAdorner, y);
        }

        SetStatus($"Moving preview object #{_selectedPreviewObjectId} to x={x}, y={y}. Release mouse to write.");
    }

    private void BeginPreviewPan(MouseButtonEventArgs e)
    {
        ClearPreviewDrag();
        ClearPreviewPan();
        _panningPreview = true;
        _previewPanMoved = false;
        _previewPanStartPoint = e.GetPosition(PreviewScrollViewer);
        _previewPanStartCanvasPoint = e.GetPosition(PreviewCanvas);
        _previewPanStartHorizontalOffset = PreviewScrollViewer.HorizontalOffset;
        _previewPanStartVerticalOffset = PreviewScrollViewer.VerticalOffset;
        PreviewCanvas.CaptureMouse();
        UpdatePreviewCursor();
    }

    private void UpdatePreviewPan(MouseEventArgs e)
    {
        if (!_panningPreview)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPreviewPan();
            return;
        }

        var point = e.GetPosition(PreviewScrollViewer);
        var dx = point.X - _previewPanStartPoint.X;
        var dy = point.Y - _previewPanStartPoint.Y;
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
        {
            _previewPanMoved = true;
        }

        PreviewScrollViewer.ScrollToHorizontalOffset(_previewPanStartHorizontalOffset - dx);
        PreviewScrollViewer.ScrollToVerticalOffset(_previewPanStartVerticalOffset - dy);
    }

    private void ClearPreviewPan()
    {
        _panningPreview = false;
        _previewPanMoved = false;
        if (PreviewCanvas.IsMouseCaptured && !_draggingPreviewObject)
        {
            PreviewCanvas.ReleaseMouseCapture();
        }

        UpdatePreviewCursor();
    }

    private void UpdatePreviewCursor()
    {
        if (PreviewCanvas is not null)
        {
            PreviewCanvas.Cursor = _previewEditMode ? Cursors.Cross : Cursors.SizeAll;
        }
    }
    private void BeginPreviewDrag(Lr2PreviewItem item, Point point)
    {
        ClearPreviewPan();
        ClearPreviewDrag();
        if (!_previewEditMode)
        {
            SetStatus("Preview is read-only. Press L to enable edit mode.");
            return;
        }


        _draggingPreviewObject = true;
        _previewDragStartPoint = point;
        _previewDragStartX = item.Object.DestX;
        _previewDragStartY = item.Object.DestY;
        _previewDragLastDx = 0;
        _previewDragLastDy = 0;
        PreviewCanvas.CaptureMouse();
    }

    private void ClearPreviewDrag()
    {
        _draggingPreviewObject = false;
        _previewDragLastDx = 0;
        _previewDragLastDy = 0;
        if (PreviewCanvas.IsMouseCaptured && !_panningPreview)
        {
            PreviewCanvas.ReleaseMouseCapture();
        }

        UpdatePreviewCursor();
    }

    private bool MoveSelectedPreviewObjectBy(int dx, int dy)
    {
        if (_document is null || _selectedPreviewObjectId is null)
        {
            SetStatus("Select a preview object first.");
            return false;
        }

        var item = _document.Objects.FirstOrDefault(candidate => candidate.Id == _selectedPreviewObjectId.Value);
        if (item is null)
        {
            SetStatus("Selected preview object is no longer available.");
            return false;
        }

        return MoveSelectedPreviewObjectTo(item.DestX + dx, item.DestY + dy);
    }

    private bool MoveSelectedPreviewObjectTo(int x, int y)
    {
        if (_document is null || _selectedPreviewObjectId is null)
        {
            SetStatus("Select a preview object first.");
            return false;
        }

        var item = _document.Objects.FirstOrDefault(candidate => candidate.Id == _selectedPreviewObjectId.Value);
        if (item is null)
        {
            SetStatus("Selected preview object is no longer available.");
            return false;
        }

        if (!_previewEditMode)
        {
            SetStatus("Preview is read-only. Press L to enable edit mode.");
            return false;
        }


        if (item.DestX == x && item.DestY == y)
        {
            return true;
        }

        item.DestX = x;
        item.DestY = y;
        _selectedPreviewObjectId = item.Id;
        if (!ApplyPreviewObjectGeometry(item))
        {
            return false;
        }

        MoveSelectedPreviewVisualTo(x, y);
        ObjectsGrid.Items.Refresh();
        ObjectsGrid.SelectedItem = item;
        UpdatePreviewSelectionPanel();
        SetStatus($"Moved preview object #{item.Id} to x={x}, y={y} ({(item.IsEditableInMain ? "main" : "include")}).");
        return true;
    }

    private bool ApplyPreviewObjectGeometry(SkinObjectView item)
    {
        try
        {
            var sourceCsv = ReadParsedLineText(item.SourceFile, item.SrcLine);
            if (sourceCsv is null)
            {
                SetStatus($"SRC line not found: {item.SourceFile}:{item.SrcLine}");
                return false;
            }

            var updatedSourceCsv = UpdatePreviewObjectSourceGeometryLine(sourceCsv, item);
            string? updatedDestinationCsv = null;
            if (item.DstLine > 0)
            {
                var destinationPath = ReadObjectDstFile(item);
                var destinationCsv = ReadParsedLineText(destinationPath, item.DstLine);
                if (destinationCsv is null)
                {
                    SetStatus($"DST line not found: {destinationPath}:{item.DstLine}");
                    return false;
                }

                updatedDestinationCsv = UpdatePreviewObjectDestinationGeometryLine(destinationCsv, item);
            }

            return ApplyPreviewCsvLines(item, updatedSourceCsv, updatedDestinationCsv);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Geometry edit failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string UpdatePreviewObjectSourceGeometryLine(string csv, SkinObjectView item)
    {
        var fields = CsvUtil.Split(csv);
        if (fields.Count == 0 || !fields[0].StartsWith("#SRC_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selected object SRC line is no longer a #SRC_ CSV row.");
        }

        CsvUtil.SetInt(fields, 3, item.SourceX);
        CsvUtil.SetInt(fields, 4, item.SourceY);
        CsvUtil.SetInt(fields, 5, item.SourceWidth);
        CsvUtil.SetInt(fields, 6, item.SourceHeight);
        return CsvUtil.Join(fields);
    }

    private static string UpdatePreviewObjectDestinationGeometryLine(string csv, SkinObjectView item)
    {
        var fields = CsvUtil.Split(csv);
        if (fields.Count == 0 || !fields[0].StartsWith("#DST_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selected object DST line is no longer a #DST_ CSV row.");
        }

        CsvUtil.SetInt(fields, 3, item.DestX);
        CsvUtil.SetInt(fields, 4, item.DestY);
        CsvUtil.SetInt(fields, 5, item.DestWidth);
        CsvUtil.SetInt(fields, 6, item.DestHeight);
        return CsvUtil.Join(fields);
    }

    private bool ApplyPreviewCsvLines(SkinObjectView item, string sourceCsv, string? destinationCsv)
    {
        var updates = new List<PreviewCsvLineUpdate>
        {
            new(item.SourceFile, item.SrcLine, sourceCsv)
        };

        if (destinationCsv is not null && item.DstLine > 0)
        {
            updates.Add(new(ReadObjectDstFile(item), item.DstLine, destinationCsv));
        }

        try
        {
            foreach (var update in updates)
            {
                if (!ReplacePreviewCodeDocumentLine(update))
                {
                    return false;
                }
            }

            _dirty = true;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV edit failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }


    private bool DeletePreviewObject(SkinObjectView item)
    {
        if (_document is null)
        {
            SetStatus("Create or open a .lr2skin file first.");
            return false;
        }

        var lineGroups = BuildPreviewObjectDeletionLineGroups(item);
        var deleteLineCount = lineGroups.Values.Sum(lines => lines.Distinct().Count());
        if (deleteLineCount == 0)
        {
            SetStatus("No editable lines found for the selected object.");
            return false;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete object #{item.Id} {item.Kind} and {deleteLineCount:N0} code line(s)?",
            "Delete object",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var group in lineGroups)
        {
            if (!RemovePreviewCodeDocumentLines(group.Key, group.Value))
            {
                return false;
            }
        }

        ClearPreviewDrag();
        _selectedPreviewObjectId = null;
        _selectedPreviewItem = null;
        ObjectsGrid.SelectedItem = null;
        _dirty = true;
        UpdateTitle();
        RefreshDocumentFromEditor();
        RenderPreview();
        SetStatus($"Deleted object #{item.Id}.");
        return true;
    }

    private Dictionary<string, List<int>> BuildPreviewObjectDeletionLineGroups(SkinObjectView item)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        AddDeletionLine(groups, item.SourceFile, item.SrcLine);

        var dstFile = ReadObjectDstFile(item);
        foreach (var frame in item.Frames)
        {
            AddDeletionLine(groups, dstFile, frame.Line);
        }

        if (item.Frames.Count == 0 && item.DstLine > 0)
        {
            AddDeletionLine(groups, dstFile, item.DstLine);
        }

        return groups;
    }

    private static void AddDeletionLine(Dictionary<string, List<int>> groups, string path, int lineNumber)
    {
        if (string.IsNullOrWhiteSpace(path) || lineNumber <= 0)
        {
            return;
        }

        if (!groups.TryGetValue(path, out var lines))
        {
            lines = new List<int>();
            groups[path] = lines;
        }

        lines.Add(lineNumber);
    }

    private bool RemovePreviewCodeDocumentLines(string path, IEnumerable<int> lineNumbers)
    {
        var document = FindPreviewCodeDocument(path);
        if (document is null)
        {
            SetStatus($"Preview code tab not found: {path}");
            return false;
        }

        var lines = SplitEditorLines(ReadPreviewCodeDocumentText(document));
        foreach (var lineNumber in lineNumbers.Distinct().OrderByDescending(line => line))
        {
            if (lineNumber <= 0 || lineNumber > lines.Count)
            {
                SetStatus($"Line {lineNumber:N0} is outside {FormatPreviewCodePathLabel(path)}.");
                return false;
            }

            lines.RemoveAt(lineNumber - 1);
        }

        WritePreviewCodeDocumentText(document, string.Join(Environment.NewLine, lines));
        return true;
    }

    private static List<string> SplitEditorLines(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
    }

    private void WritePreviewCodeDocumentText(PreviewCodeDocument document, string updatedText)
    {
        document.Text = updatedText;
        document.IsDirty = true;
        _dirty = true;

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
    }
    private bool TryReadPreviewCsvLine(TextBox box, string expectedPrefix, string label, out string csv)
    {
        csv = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(csv))
        {
            SetStatus($"{label} is empty.");
            box.Focus();
            return false;
        }

        if (csv.Contains('\r') || csv.Contains('\n'))
        {
            SetStatus($"{label} must be a single CSV line.");
            box.Focus();
            return false;
        }

        var fields = CsvUtil.Split(csv);
        if (fields.Count == 0 || !fields[0].Trim().StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"{label} must start with {expectedPrefix}.");
            box.Focus();
            return false;
        }

        return true;
    }
    private Lr2PreviewItem? FindPreviewItemAt(Point point)
    {
        for (var i = _previewItems.Count - 1; i >= 0; i--)
        {
            var item = _previewItems[i];
            var dest = item.Destination;
            if (point.X >= dest.X && point.X <= dest.X + dest.Width &&
                point.Y >= dest.Y && point.Y <= dest.Y + dest.Height)
            {
                return item;
            }
        }

        return null;
    }

    private void AddPreviewSelectionAdorner(Lr2PreviewItem item)
    {
        var dest = item.Destination;
        var outline = new Rectangle
        {
            Width = dest.Width,
            Height = dest.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(34, 211, 238)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 6, 3 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };

        ApplyRotation(outline, item.Angle, item.Center);
        _selectedPreviewAdorner = outline;
        Canvas.SetLeft(outline, dest.X);
        Canvas.SetTop(outline, dest.Y);
        PreviewCanvas.Children.Add(outline);
    }

    private void UpdatePreviewSelectionPanel()
    {
        if (PreviewSelectionBox is null) return;

        if (_document is null)
        {
            UpdatePreviewSelectionThumbnail(null, null);
            UpdatePreviewCsvEditor(null);
            PreviewSelectionBox.Text = $"Mode: {ReadPreviewEditModeLabel()}\r\nOpen a .lr2skin file.";
            return;
        }

        if (_selectedPreviewObjectId is null)
        {
            UpdatePreviewSelectionThumbnail(null, null);
            UpdatePreviewCsvEditor(null);
            PreviewSelectionBox.Text = $"Mode: {ReadPreviewEditModeLabel()}\r\nNo preview object selected.";
            return;
        }

        var skinObject = _document.Objects.FirstOrDefault(item => item.Id == _selectedPreviewObjectId.Value);
        if (skinObject is null)
        {
            UpdatePreviewSelectionThumbnail(null, null);
            UpdatePreviewCsvEditor(null);
            PreviewSelectionBox.Text = $"Object #{_selectedPreviewObjectId.Value} is no longer in the parsed skin.";
            return;
        }

        UpdatePreviewSelectionThumbnail(skinObject, _selectedPreviewItem);
        UpdatePreviewCsvEditor(skinObject);
        PreviewSelectionBox.Text = FormatPreviewSelection(skinObject, _selectedPreviewItem);
    }

    private void UpdatePreviewCsvEditor(SkinObjectView? skinObject)
    {
        if (PreviewSourceCsvBox is null || PreviewDestinationCsvBox is null ||
            ApplyPreviewCsvButton is null || ResetPreviewCsvButton is null || PreviewCsvStatusText is null)
        {
            return;
        }

        if (skinObject is null)
        {
            PreviewSourceCsvBox.Text = string.Empty;
            PreviewDestinationCsvBox.Text = string.Empty;
            PreviewSourceCsvBox.IsEnabled = false;
            PreviewDestinationCsvBox.IsEnabled = false;
            ApplyPreviewCsvButton.IsEnabled = false;
            ResetPreviewCsvButton.IsEnabled = false;
            PreviewCsvStatusText.Text = string.Empty;
            return;
        }

        PreviewSourceCsvBox.Text = ReadParsedLineText(skinObject.SourceFile, skinObject.SrcLine) ?? string.Empty;
        PreviewDestinationCsvBox.Text = skinObject.DstLine > 0
            ? ReadParsedLineText(ReadObjectDstFile(skinObject), skinObject.DstLine) ?? string.Empty
            : string.Empty;
        PreviewSourceCsvBox.IsEnabled = true;
        PreviewDestinationCsvBox.IsEnabled = skinObject.DstLine > 0;
        ApplyPreviewCsvButton.IsEnabled = true;
        ResetPreviewCsvButton.IsEnabled = true;
        PreviewCsvStatusText.Text = skinObject.IsEditableInMain ? "main" : "include";
    }

    private string? ReadParsedLineText(string path, int lineNumber)
    {
        if (_document is null || string.IsNullOrWhiteSpace(path) || lineNumber <= 0)
        {
            return null;
        }

        return _document.Lines.FirstOrDefault(line =>
            line.SourceLine == lineNumber &&
            IsSamePath(line.SourcePath, path))?.RawText.Trim();
    }

    private void UpdatePreviewSelectionThumbnail(SkinObjectView? skinObject, Lr2PreviewItem? visibleItem)
    {
        if (PreviewSelectionImage is null || PreviewSelectionImageLabel is null) return;

        PreviewSelectionImage.Source = null;
        PreviewSelectionImageLabel.Visibility = Visibility.Visible;

        if (skinObject is null)
        {
            PreviewSelectionImageLabel.Text = "No image selected";
            return;
        }

        if (string.IsNullOrWhiteSpace(skinObject.ImagePath))
        {
            PreviewSelectionImageLabel.Text = "No image";
            return;
        }

        var thumbnailItem = visibleItem ?? new Lr2PreviewItem(
            skinObject,
            new PreviewRect(skinObject.DestX, skinObject.DestY, skinObject.DestWidth, skinObject.DestHeight),
            0,
            skinObject.Id,
            1.0,
            255,
            255,
            255,
            0,
            0,
            0,
            0);

        if (_bitmapFactory.TryCreateFrameBitmap(thumbnailItem, out var bitmap))
        {
            PreviewSelectionImage.Source = bitmap;
            PreviewSelectionImageLabel.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewSelectionImageLabel.Text = "Image unavailable";
    }
    private string ReadPreviewEditModeLabel()
    {
        return _previewEditMode ? "EDIT" : "READ-ONLY";
    }
    private string FormatPreviewSelection(SkinObjectView skinObject, Lr2PreviewItem? visibleItem)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mode: {ReadPreviewEditModeLabel()}");
        builder.AppendLine($"Object #{skinObject.Id}");
        builder.AppendLine($"Group: {ReadSkinHelpGroup(skinObject.Kind)}");
        builder.AppendLine($"Kind: {skinObject.Kind}");
        builder.AppendLine($"Image: {FormatMissing(skinObject.ImagePath)}");
        builder.AppendLine($"Source file: {skinObject.SourceFile}");
        builder.AppendLine($"SRC line: {skinObject.SrcLine}");
        builder.AppendLine($"DST file: {(skinObject.DstLine > 0 ? ReadObjectDstFile(skinObject) : "(none)")}");
        builder.AppendLine($"DST line: {(skinObject.DstLine > 0 ? skinObject.DstLine.ToString(CultureInfo.InvariantCulture) : "none")}");
        builder.AppendLine($"Editable: {(skinObject.IsEditableInMain ? "main file" : "include file")}");
        builder.AppendLine();

        builder.AppendLine("Current render");
        if (visibleItem is null)
        {
            builder.AppendLine("visible: no at current preview time/options");
        }
        else
        {
            builder.AppendLine($"visible: yes");
            builder.AppendLine($"position: {FormatPreviewRect(visibleItem.Destination)}");
            builder.AppendLine($"source frame: {visibleItem.SourceFrame}");
            builder.AppendLine($"opacity: {FormatDouble(visibleItem.Opacity)}");
            builder.AppendLine($"rgb: {visibleItem.Red},{visibleItem.Green},{visibleItem.Blue}");
            builder.AppendLine($"blend/filter: {visibleItem.Blend}/{visibleItem.Filter}");
            builder.AppendLine($"angle/center: {FormatDouble(visibleItem.Angle)}/{visibleItem.Center}");
            builder.AppendLine($"sort id: {visibleItem.SortId}");
        }
        builder.AppendLine();

        builder.AppendLine("SRC fields");
        builder.AppendLine($"index: {skinObject.SourceIndex}");
        builder.AppendLine($"graph: {skinObject.SourceGraph}");
        builder.AppendLine($"rect: x={skinObject.SourceX}, y={skinObject.SourceY}, w={skinObject.SourceWidth}, h={skinObject.SourceHeight}");
        builder.AppendLine($"div: {skinObject.SourceDivX} x {skinObject.SourceDivY}");
        builder.AppendLine($"cycle/timer: {skinObject.SourceCycle}/{skinObject.SourceTimer}");
        builder.AppendLine($"op: {skinObject.SourceOp1}, {skinObject.SourceOp2}, {skinObject.SourceOp3}, {skinObject.SourceOp4}, {skinObject.SourceOp5}");
        builder.AppendLine();

        builder.AppendLine("DST base");
        builder.AppendLine($"rect: x={skinObject.DestX}, y={skinObject.DestY}, w={skinObject.DestWidth}, h={skinObject.DestHeight}");
        builder.AppendLine($"loop/timer: {skinObject.DestLoop}/{skinObject.DestTimer}");
        builder.AppendLine($"op: {skinObject.DestOp1}, {skinObject.DestOp2}, {skinObject.DestOp3}, {skinObject.DestOp4}, {skinObject.DestOp5}");
        builder.AppendLine();

        builder.AppendLine($"DST frames ({skinObject.Frames.Count})");
        foreach (var frame in skinObject.Frames)
        {
            builder.AppendLine($"line {frame.Line}: t={frame.Time}, x={FormatDouble(frame.X)}, y={FormatDouble(frame.Y)}, w={FormatDouble(frame.Width)}, h={FormatDouble(frame.Height)}, acc={frame.Acc}, a={frame.Alpha}, rgb={frame.Red}/{frame.Green}/{frame.Blue}, blend={frame.Blend}, filter={frame.Filter}, angle={FormatDouble(frame.Angle)}, center={frame.Center}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatPreviewRect(PreviewRect rect)
    {
        return $"x={FormatDouble(rect.X)}, y={FormatDouble(rect.Y)}, w={FormatDouble(rect.Width)}, h={FormatDouble(rect.Height)}";
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(none)" : value;
    }

    private bool IsMainDocumentPath(string path)
    {
        return _document is not null && IsSamePath(_document.MainPath, path);
    }

    private static string ReadObjectDstFile(SkinObjectView item)
    {
        return string.IsNullOrWhiteSpace(item.DstFile) ? item.SourceFile : item.DstFile;
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(NormalizeComparablePath(left), NormalizeComparablePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return IOPath.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static void ApplyRotation(FrameworkElement element, double angle, int center)
    {
        if (Math.Abs(angle) < 0.001) return;
        element.RenderTransformOrigin = CenterToOrigin(center);
        element.RenderTransform = new RotateTransform(angle);
    }

    private static Point CenterToOrigin(int center)
    {
        return center switch
        {
            1 => new Point(0.0, 1.0),
            2 => new Point(0.5, 1.0),
            3 => new Point(1.0, 1.0),
            4 => new Point(0.0, 0.5),
            6 => new Point(1.0, 0.5),
            7 => new Point(0.0, 0.0),
            8 => new Point(0.5, 0.0),
            9 => new Point(1.0, 0.0),
            _ => new Point(0.5, 0.5)
        };
    }

    private static Rect NormalizeDestination(SkinObjectView item)
    {
        var width = item.DestWidth;
        var height = item.DestHeight;
        var x = item.DestX;
        var y = item.DestY;

        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        return new Rect(x, y, width, height);
    }

    private double ReadPreviewTime()
    {
        return TryReadPreviewTime(out var timeMs) ? timeMs : 1000;
    }

    private Lr2PreviewClock ReadPreviewClock()
    {
        return Lr2PreviewClock.Create(ReadPreviewTime(), ReadPreviewMode());
    }

    private string ReadPreviewMode()
    {
        return PreviewModeBox.SelectedItem is ComboBoxItem item && item.Tag is string mode ? mode : "playing";
    }

    private bool TryReadPreviewTime(out double timeMs)
    {
        if (double.TryParse(PreviewTimeBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out timeMs))
        {
            timeMs = Math.Clamp(timeMs, PreviewTimeSlider.Minimum, PreviewTimeSlider.Maximum);
            return true;
        }

        timeMs = 0;
        return false;
    }

    private bool TryReadDstSpec(out Lr2DstSpec spec)
    {
        spec = new Lr2DstSpec(0, 0, 0, 1, 1, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        if (!TryReadInt(CreateTimeBox, "time", out var time) ||
            !TryReadInt(CreateXBox, "x", out var x) ||
            !TryReadInt(CreateYBox, "y", out var y) ||
            !TryReadPositiveInt(CreateWidthBox, "width", out var width) ||
            !TryReadPositiveInt(CreateHeightBox, "height", out var height) ||
            !TryReadInt(CreateAccBox, "acc", out var acc) ||
            !TryReadInt(CreateAlphaBox, "alpha", out var alpha) ||
            !TryReadInt(CreateRedBox, "red", out var red) ||
            !TryReadInt(CreateGreenBox, "green", out var green) ||
            !TryReadInt(CreateBlueBox, "blue", out var blue) ||
            !TryReadInt(CreateBlendBox, "blend", out var blend) ||
            !TryReadInt(CreateFilterBox, "filter", out var filter) ||
            !TryReadDouble(CreateAngleBox, "angle", out var angle) ||
            !TryReadInt(CreateCenterBox, "center", out var center) ||
            !TryReadInt(CreateLoopBox, "loop", out var loop) ||
            !TryReadInt(CreateTimerBox, "timer", out var timer) ||
            !TryReadInt(CreateOp1Box, "op1", out var op1) ||
            !TryReadInt(CreateOp2Box, "op2", out var op2) ||
            !TryReadInt(CreateOp3Box, "op3", out var op3) ||
            !TryReadInt(CreateOp4Box, "op4", out var op4) ||
            !TryReadInt(CreateOp5Box, "op5", out var op5))
        {
            return false;
        }

        spec = new Lr2DstSpec(time, x, y, width, height, acc, alpha, red, green, blue, blend, filter, angle, center, loop, timer, op1, op2, op3, op4, op5);
        return true;
    }

    private bool TryReadDouble(TextBox box, string label, out double value)
    {
        if (double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        SetStatus($"{label} is not a valid number.");
        box.Focus();
        return false;
    }

    private bool TryReadPositiveInt(TextBox box, string label, out int value)
    {
        if (TryReadInt(box, label, out value) && value > 0) return true;
        SetStatus($"{label} must be greater than zero.");
        box.Focus();
        return false;
    }

    private bool TryReadInt(TextBox box, string label, out int value)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        SetStatus($"{label} is not a valid number.");
        box.Focus();
        return false;
    }

    private bool TryGetDocumentDirectory(out string directory)
    {
        directory = string.Empty;
        if (_document is null || string.IsNullOrWhiteSpace(_document.MainPath))
        {
            SetStatus("Create or open a .lr2skin file first.");
            return false;
        }

        directory = IOPath.GetDirectoryName(IOPath.GetFullPath(_document.MainPath)) ?? Environment.CurrentDirectory;
        return true;
    }

    private bool TryGetSelectedImageSlot(out SkinImageSlot imageSlot)
    {
        if (ImageAssetBox.SelectedItem is SkinImageSlot selected)
        {
            imageSlot = selected;
            return true;
        }

        if (_document?.ImageSlots.Count > 0)
        {
            imageSlot = _document.ImageSlots[0];
            ImageAssetBox.SelectedIndex = 0;
            return true;
        }

        imageSlot = null!;
        SetStatus("Add or select an image asset first.");
        return false;
    }

    private string ReadCreateKind()
    {
        return CreateKindBox.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "image";
    }

    private void AppendGeneratedLines(IReadOnlyList<string> lines, string label)
    {
        // v1 deliberately appends to the main .lr2skin text only. Include editing is read/preview-only
        // until we have a safer per-include save policy.
        var text = CodeEditor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder(text);
        if (builder.Length > 0 && !text.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        builder.AppendLine();
        builder.AppendLine($"// SkinEditorNext generated {label}");
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        SetEditorText(builder.ToString().Replace("\n", Environment.NewLine), markDirty: true);
    }

    private static bool IsSupportedImageExtension(string extension)
    {
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueAssetPath(string directory, string fileName)
    {
        var stem = IOPath.GetFileNameWithoutExtension(fileName);
        var extension = IOPath.GetExtension(fileName);
        var candidate = IOPath.Combine(directory, fileName);
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = IOPath.Combine(directory, $"{stem}_{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = IOPath.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "new" : cleaned;
    }

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

    private void SetEmptyState()
    {
        FilePathText.Text = "No file loaded.";
        EncodingText.Text = string.Empty;
        StatusText.Text = "Open a .lr2skin file to start.";
        FooterText.Text = "Ready.";
        DiagnosticsBox.Text = string.Empty;
        ObjectsGrid.ItemsSource = null;
        ImageAssetBox.ItemsSource = null;
        AssetsSummaryText.Text = "0 image(s)";
        UpdateAssetPreview();
        _skinHelpRows.Clear();
        ApplySkinHelpFilter();
        LoadSkinHelpEasyEditor(null);
        RefreshSkinIfRows();
        ResolutionWidthBox.Text = "640";
        ResolutionHeightBox.Text = "480";
        PreviewCanvas.Width = 640;
        PreviewCanvas.Height = 480;
        PreviewOverlay.Text = "Open a .lr2skin file.";
        ResetPreviewZoom();
        UpdatePreviewCursor();
        _selectedPreviewObjectId = null;
        _selectedPreviewItem = null;
        ClearPreviewDrag();
        UpdatePreviewSelectionPanel();
        PreviewTimeBox.Text = "1000";
        PreviewTimeSlider.Value = 1000;
        PreviewModeBox.SelectedIndex = 0;
        _previewCodeRefreshTimer.Stop();
        _previewCodeDocuments.Clear();
        if (PreviewCodeTabs is not null)
        {
            PreviewCodeTabs.Items.Clear();
        }
        if (PreviewCodeStatusText is not null)
        {
            PreviewCodeStatusText.Text = string.Empty;
        }
        UpdateTitle();
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void UpdateTitle()
    {
        var name = _document is null || string.IsNullOrWhiteSpace(_document.MainPath)
            ? "LR2 Skin Editor Next"
            : IOPath.GetFileName(_document.MainPath);
        Title = (_dirty ? "*" : string.Empty) + name + " - LR2 Skin Editor Next";
    }

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
