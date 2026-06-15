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

    private void SetEmptyState()
    {
        FilePathText.Text = "No file loaded.";
        EncodingText.Text = string.Empty;
        StatusText.Text = "Open a .lr2skin file to start.";
        FooterText.Text = "Ready.";
        DiagnosticsBox.Text = string.Empty;
        ObjectsGrid.ItemsSource = null;
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
