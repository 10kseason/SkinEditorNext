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
}
