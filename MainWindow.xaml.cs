using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SkinEditorNext.Models;
using SkinEditorNext.Services;
using IOPath = System.IO.Path;

namespace SkinEditorNext;

public partial class MainWindow : Window
{
    private readonly Lr2SkinParser _parser = new();
    private readonly Lr2PreviewEvaluator _previewEvaluator = new();
    private readonly Lr2BitmapFactory _bitmapFactory = new();
    private readonly List<SkinHelpEntry> _skinHelpTemplates = new();
    private readonly Dictionary<string, SkinHelpEntry> _skinHelpTemplatesByCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _skinHelpGroupByCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkinHelpEntry> _skinHelpRows = new();
    private readonly List<SkinImportEntry> _skinImportEntries = new();
    private readonly List<Lr2PreviewItem> _previewItems = new();
    private Lr2SkinDocument? _document;
    private bool _loadingEditor;
    private bool _syncingPreviewTime;
    private bool _dirty;
    private int? _selectedPreviewObjectId;
    private Lr2PreviewItem? _selectedPreviewItem;
    private bool _previewEditMode;
    private bool _draggingPreviewObject;
    private Point _previewDragStartPoint;
    private int _previewDragStartX;
    private int _previewDragStartY;
    private int _previewDragLastDx;
    private int _previewDragLastDy;

    public MainWindow()
    {
        InitializeComponent();
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
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var text = Lr2SkinWriter.CreateNewSkin(dialog.Settings);
            File.WriteAllText(saveDialog.FileName, text, encoding);
            _bitmapFactory.Clear();
            _document = _parser.ParseMainText(saveDialog.FileName, text, encoding);
            SetEditorText(text, markDirty: false);
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

    private void ObjectsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ObjectsGrid.SelectedItem is not SkinObjectView item)
        {
            SelectedObjectText.Text = string.Empty;
            return;
        }

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
        UpdateTitle();
    }

    private void SkinHelpSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySkinHelpFilter();
    }

    private void SkinHelpModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySkinHelpFilter();
    }

    private void SkinHelpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SkinHelpDetailBox.Text = SkinHelpGrid.SelectedItem is SkinHelpEntry entry ? entry.Detail : string.Empty;
    }

    private void SkinHelpGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InsertSelectedSkinHelpLine();
    }

    private void InsertSkinHelp_Click(object sender, RoutedEventArgs e)
    {
        InsertSelectedSkinHelpLine();
    }

    private bool OpenDocument(string path)
    {
        try
        {
            _bitmapFactory.Clear();
            _document = _parser.Load(path);
            SetEditorText(_document.MainText, markDirty: false);
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
            var encoding = _document?.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(path, CodeEditor.Text, encoding);
            _dirty = false;
            _document = _parser.ParseMainText(path, CodeEditor.Text, encoding);
            UpdateDocumentViews();
            RenderPreview();
            SetStatus($"Saved {IOPath.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshDocumentFromEditor(int? selectedObjectId = null)
    {
        var path = _document?.MainPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = IOPath.Combine(Environment.CurrentDirectory, "untitled.lr2skin");
        }

        var encoding = _document?.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _document = _parser.ParseMainText(path, CodeEditor.Text, encoding);
        UpdateDocumentViews(selectedObjectId);
    }

    private void UpdateDocumentViews(int? selectedObjectId = null)
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

    private void RenderPreview()
    {
        PreviewCanvas.Children.Clear();
        _previewItems.Clear();
        _selectedPreviewItem = null;

        if (_document is null)
        {
            PreviewCanvas.Width = 640;
            PreviewCanvas.Height = 480;
            PreviewOverlay.Text = "Open a .lr2skin file.";
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
        PreviewOverlay.Text = $"{resolution.Width} x {resolution.Height} / {renderItems.Count:N0} visible / {_document.Objects.Count:N0} objects / {(int)clock.TimeMs} ms / {clock.ModeName} / {ReadPreviewEditModeLabel()}";

        PreviewCanvas.Children.Add(new Rectangle
        {
            Width = resolution.Width,
            Height = resolution.Height,
            Fill = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            IsHitTestVisible = false
        });

        foreach (var item in renderItems)
        {
            AddPreviewObject(item);
        }

        PreviewCanvas.Children.Add(new Rectangle
        {
            Width = resolution.Width,
            Height = resolution.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        });

        if (_selectedPreviewItem is not null)
        {
            AddPreviewSelectionAdorner(_selectedPreviewItem);
        }

        UpdatePreviewSelectionPanel();
    }

    private void AddPreviewObject(Lr2PreviewItem item)
    {
        var dest = item.Destination;
        if (dest.Width <= 0 || dest.Height <= 0) return;

        var skinObject = item.Object;
        if (skinObject.Kind.Equals("#SRC_TEXT", StringComparison.OrdinalIgnoreCase))
        {
            AddTextPlaceholder(item, new Rect(dest.X, dest.Y, dest.Width, dest.Height));
            return;
        }

        if (_bitmapFactory.TryCreateFrameBitmap(item, out var frameBitmap))
        {
            var image = new Image
            {
                Source = frameBitmap,
                Width = dest.Width,
                Height = dest.Height,
                Stretch = Stretch.Fill,
                Opacity = item.Opacity,
                IsHitTestVisible = false
            };

            ApplyRotation(image, item.Angle, item.Center);
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, dest.X);
            Canvas.SetTop(image, dest.Y);
            PreviewCanvas.Children.Add(image);
            return;
        }

        var outline = new Rectangle
        {
            Width = dest.Width,
            Height = dest.Height,
            Stroke = skinObject.Kind.Equals("#SRC_IMAGE", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                : new SolidColorBrush(Color.FromRgb(250, 204, 21)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            IsHitTestVisible = false
        };
        ApplyRotation(outline, item.Angle, item.Center);
        Canvas.SetLeft(outline, dest.X);
        Canvas.SetTop(outline, dest.Y);
        PreviewCanvas.Children.Add(outline);
    }

    private void AddTextPlaceholder(Lr2PreviewItem item, Rect dest)
    {
        var placeholderColor = Color.FromRgb((byte)item.Red, (byte)item.Green, (byte)item.Blue);
        var isGuide = item.Object.SourceIndex is >= 9000 and < 9100;
        var label = ReadTextPlaceholderLabel(item.Object);
        var border = new Border
        {
            Width = dest.Width,
            Height = dest.Height,
            Opacity = item.Opacity,
            BorderBrush = new SolidColorBrush(placeholderColor),
            BorderThickness = new Thickness(isGuide ? 2 : 1),
            Background = new SolidColorBrush(Color.FromArgb(isGuide ? (byte)92 : (byte)32, placeholderColor.R, placeholderColor.G, placeholderColor.B)),
            Child = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(ReadContrastingTextColor(placeholderColor)),
                FontFamily = new FontFamily("Consolas"),
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

        ApplyRotation(border, item.Angle, item.Center);
        Canvas.SetLeft(border, dest.X);
        Canvas.SetTop(border, dest.Y);
        PreviewCanvas.Children.Add(border);
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

    private void PreviewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null) return;

        PreviewCanvas.Focus();
        var point = e.GetPosition(PreviewCanvas);
        var item = FindPreviewItemAt(point);
        if (item is null)
        {
            ClearPreviewDrag();
            _selectedPreviewObjectId = null;
            _selectedPreviewItem = null;
            RenderPreview();
            SetStatus($"No preview object at {FormatDouble(point.X)},{FormatDouble(point.Y)}.");
            return;
        }

        _selectedPreviewObjectId = item.Object.Id;
        _selectedPreviewItem = item;
        ObjectsGrid.SelectedItem = _document.Objects.FirstOrDefault(candidate => candidate.Id == item.Object.Id);
        BeginPreviewDrag(item, point);
        RenderPreview();
        SetStatus($"Selected preview object #{item.Object.Id} {item.Object.Kind} at {FormatPreviewRect(item.Destination)}.");
        e.Handled = true;
    }

    private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
    {
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
        if (MoveSelectedPreviewObjectTo(_previewDragStartX + dx, _previewDragStartY + dy))
        {
            e.Handled = true;
        }
    }

    private void PreviewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingPreviewObject) return;
        ClearPreviewDrag();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.L || !PreviewTab.IsSelected)
        {
            return;
        }

        TogglePreviewEditMode();
        PreviewCanvas.Focus();
        e.Handled = true;
    }
    private void PreviewCanvas_KeyDown(object sender, KeyEventArgs e)
    {
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
        UpdatePreviewSelectionPanel();
        SetStatus(_previewEditMode
            ? "Preview edit mode enabled. Drag or use arrow keys to move by 1px. Press L for read-only."
            : "Preview read-only mode enabled. Press L for edit mode.");
    }
    private void BeginPreviewDrag(Lr2PreviewItem item, Point point)
    {
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
        if (PreviewCanvas.IsMouseCaptured)
        {
            PreviewCanvas.ReleaseMouseCapture();
        }
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

        RefreshDocumentFromEditor(item.Id);
        ObjectsGrid.SelectedItem = _document?.Objects.FirstOrDefault(candidate => candidate.Id == item.Id);
        RenderPreview();
        SetStatus($"Moved preview object #{item.Id} to x={x}, y={y} ({(item.IsEditableInMain ? "main" : "include")}).");
        return true;
    }

    private bool ApplyPreviewObjectGeometry(SkinObjectView item)
    {
        if (item.IsEditableInMain)
        {
            SetEditorText(Lr2SkinParser.UpdateObjectGeometry(CodeEditor.Text, item), markDirty: true);
            return true;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(item.SourceFile) || !File.Exists(item.SourceFile))
            {
                SetStatus($"Include file not found: {item.SourceFile}");
                return false;
            }

            var bytes = File.ReadAllBytes(item.SourceFile);
            var encoding = Lr2SkinParser.DetectEncoding(bytes);
            var includeText = encoding.GetString(bytes);
            var updatedText = Lr2SkinParser.UpdateObjectGeometry(includeText, item);
            File.WriteAllText(item.SourceFile, updatedText, encoding);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Include edit failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
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
            PreviewSelectionBox.Text = $"Mode: {ReadPreviewEditModeLabel()}\r\nOpen a .lr2skin file.";
            return;
        }

        if (_selectedPreviewObjectId is null)
        {
            UpdatePreviewSelectionThumbnail(null, null);
            PreviewSelectionBox.Text = $"Mode: {ReadPreviewEditModeLabel()}\r\nNo preview object selected.";
            return;
        }

        var skinObject = _document.Objects.FirstOrDefault(item => item.Id == _selectedPreviewObjectId.Value);
        if (skinObject is null)
        {
            UpdatePreviewSelectionThumbnail(null, null);
            PreviewSelectionBox.Text = $"Object #{_selectedPreviewObjectId.Value} is no longer in the parsed skin.";
            return;
        }

        UpdatePreviewSelectionThumbnail(skinObject, _selectedPreviewItem);
        PreviewSelectionBox.Text = FormatPreviewSelection(skinObject, _selectedPreviewItem);
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
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                var rawLine = line.Trim();
                if (rawLine.Length == 0) continue;

                var fields = CsvUtil.Split(rawLine);
                if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0])) continue;

                var command = fields[0].Trim();
                var arguments = string.Join(", ", fields.Skip(1).Select(field => field.Trim()));
                var entry = new SkinHelpEntry("Helper", string.Empty, ReadSkinHelpGroup(command), command, string.Empty, arguments, rawLine, isTemplate: true, rawLine);
                _skinHelpTemplates.Add(entry);
                _skinHelpTemplatesByCommand[command] = entry;
            }
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

            var arguments = FormatSkinHelpArguments(line, template);
            var position = FormatSkinHelpPosition(line);
            var detail = FormatSkinHelpDetail(line, template, arguments, position);
            _skinHelpRows.Add(new SkinHelpEntry(
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

    private void ApplySkinHelpFilter()
    {
        if (SkinHelpGrid is null || SkinHelpSearchBox is null || SkinHelpSummaryText is null) return;

        var sourceRows = ReadSkinHelpMode() == "templates" || (_document is null && _skinHelpRows.Count == 0)
            ? _skinHelpTemplates
            : _skinHelpRows;
        var query = SkinHelpSearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? sourceRows
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

        SkinHelpGrid.ItemsSource = null;
        SkinHelpGrid.ItemsSource = filtered;
        var label = ReferenceEquals(sourceRows, _skinHelpTemplates) ? "template" : "skin row";
        SkinHelpSummaryText.Text = $"{filtered.Count:N0} / {sourceRows.Count:N0} {label}(s)";
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

    private void SelectCurrentSkinHelpMode()
    {
        if (SkinHelpModeBox is not null && SkinHelpModeBox.SelectedIndex != 0)
        {
            SkinHelpModeBox.SelectedIndex = 0;
        }
    }
    private string ReadSkinHelpGroup(string command)
    {
        return _skinHelpGroupByCommand.TryGetValue(command, out var group) ? group : string.Empty;
    }

    private void LoadSkinObjectGroups(string path)
    {
        var currentGroup = string.Empty;
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('$'))
            {
                currentGroup = line;
                continue;
            }

            if (line.StartsWith('#') && currentGroup.Length > 0)
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
    private static string FormatSkinHelpArguments(SkinCommandLine line, SkinHelpEntry? template)
    {
        if (line.Fields.Count <= 1) return string.Empty;

        IReadOnlyList<string> templateFields = template is null ? Array.Empty<string>() : CsvUtil.Split(template.RawLine);
        var parts = new List<string>();
        for (var i = 1; i < line.Fields.Count; i++)
        {
            parts.Add($"{i}:{ReadSkinHelpFieldName(templateFields, i)}={line.Fields[i].Trim()}");
        }

        return string.Join(", ", parts);
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
        ResolutionWidthBox.Text = "640";
        ResolutionHeightBox.Text = "480";
        PreviewCanvas.Width = 640;
        PreviewCanvas.Height = 480;
        PreviewOverlay.Text = "Open a .lr2skin file.";
        _selectedPreviewObjectId = null;
        _selectedPreviewItem = null;
        ClearPreviewDrag();
        UpdatePreviewSelectionPanel();
        PreviewTimeBox.Text = "1000";
        PreviewTimeSlider.Value = 1000;
        PreviewModeBox.SelectedIndex = 0;
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

    private sealed record SkinImportEntry(string FullPath, string RelativePath, string DisplayName);
}
