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
}
