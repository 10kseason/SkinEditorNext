using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
    private Lr2SkinDocument? _document;
    private bool _loadingEditor;
    private bool _syncingPreviewTime;
    private bool _dirty;

    public MainWindow()
    {
        InitializeComponent();
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

    private void ApplyObject_Click(object sender, RoutedEventArgs e)
    {
        if (ObjectsGrid.SelectedItem is not SkinObjectView item)
        {
            SetStatus("No object selected.");
            return;
        }

        if (!item.IsEditableInMain)
        {
            SetStatus("Selected object is from an include file; edit that include in code mode or open it directly.");
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

        SetEditorText(Lr2SkinParser.UpdateObjectGeometry(CodeEditor.Text, item), markDirty: true);
        RefreshDocumentFromEditor(item.Id);
        RenderPreview();
        SetStatus($"Applied object geometry for #{item.Id}.");
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
            // New assets are copied next to the skin so exported .lr2skin files stay portable.
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
                var relativePath = IOPath.GetRelativePath(directory, destinationPath);
                lines.Add(Lr2SkinWriter.ImageLine(relativePath));
            }

            if (lines.Count == 0) return;
            AppendGeneratedLines(lines, "image asset");
            _bitmapFactory.Clear();
            RefreshDocumentFromEditor();
            ImageAssetBox.SelectedItem = _document?.ImageSlots.LastOrDefault();
            RenderPreview();
            SetStatus($"Added {lines.Count} image asset(s).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Add image failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void OpenDocument(string path)
    {
        try
        {
            _bitmapFactory.Clear();
            _document = _parser.Load(path);
            SetEditorText(_document.MainText, markDirty: false);
            UpdateDocumentViews();
            RenderPreview();
            SetStatus($"Opened {IOPath.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

        ObjectsGrid.ItemsSource = null;
        ObjectsGrid.ItemsSource = _document.Objects;

        if (selectedObjectId is not null)
        {
            ObjectsGrid.SelectedItem = _document.Objects.FirstOrDefault(item => item.Id == selectedObjectId.Value);
        }

        FooterText.Text = $"{_document.Lines.Count:N0} parsed lines, {_document.Objects.Count:N0} objects, resolution {_document.Resolution}";
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

        if (_document is null)
        {
            PreviewCanvas.Width = 640;
            PreviewCanvas.Height = 480;
            PreviewOverlay.Text = "Open a .lr2skin file.";
            return;
        }

        var resolution = _document.Resolution.IsValid ? _document.Resolution : ResolutionInfo.Default;
        var clock = ReadPreviewClock();
        var renderItems = _previewEvaluator.Evaluate(_document, clock, maxItems: 3000);
        PreviewCanvas.Width = resolution.Width;
        PreviewCanvas.Height = resolution.Height;
        PreviewOverlay.Text = $"{resolution.Width} x {resolution.Height} / {renderItems.Count:N0} visible / {_document.Objects.Count:N0} objects / {(int)clock.TimeMs} ms / {clock.ModeName}";

        PreviewCanvas.Children.Add(new Rectangle
        {
            Width = resolution.Width,
            Height = resolution.Height,
            Fill = new SolidColorBrush(Color.FromRgb(17, 24, 39))
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
            StrokeThickness = 1
        });
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
        var border = new Border
        {
            Width = dest.Width,
            Height = dest.Height,
            Opacity = item.Opacity,
            BorderBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(32, (byte)item.Red, (byte)item.Green, (byte)item.Blue)),
            Child = new TextBlock
            {
                Text = "TEXT",
                Foreground = new SolidColorBrush(Color.FromRgb((byte)item.Red, (byte)item.Green, (byte)item.Blue)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = Math.Clamp(dest.Height * 0.45, 10, 28),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            },
            IsHitTestVisible = false
        };

        ApplyRotation(border, item.Angle, item.Center);
        Canvas.SetLeft(border, dest.X);
        Canvas.SetTop(border, dest.Y);
        PreviewCanvas.Children.Add(border);
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
        ResolutionWidthBox.Text = "640";
        ResolutionHeightBox.Text = "480";
        PreviewCanvas.Width = 640;
        PreviewCanvas.Height = 480;
        PreviewOverlay.Text = "Open a .lr2skin file.";
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
}
