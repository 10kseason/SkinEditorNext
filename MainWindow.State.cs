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
}
