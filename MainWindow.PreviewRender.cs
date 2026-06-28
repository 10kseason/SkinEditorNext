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
}
