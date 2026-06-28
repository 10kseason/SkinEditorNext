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
}
