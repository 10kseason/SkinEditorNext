using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkinEditorNext.Services;

namespace SkinEditorNext;

public partial class NewSkinDialog : Window
{
    public NewSkinSettings Settings { get; private set; } = Lr2SkinWriter.DefaultSettings;

    public NewSkinDialog()
    {
        InitializeComponent();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSkinType(out var skinType) ||
            !TryReadPositiveInt(WidthBox, "width", out var width) ||
            !TryReadPositiveInt(HeightBox, "height", out var height))
        {
            return;
        }

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            StatusText.Text = "title is required.";
            TitleBox.Focus();
            return;
        }

        Settings = new NewSkinSettings(
            skinType,
            title,
            MakerBox.Text.Trim(),
            ThumbnailBox.Text.Trim(),
            width,
            height);

        DialogResult = true;
    }

    private bool TryReadSkinType(out int skinType)
    {
        skinType = 0;
        if (SkinTypeBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out skinType))
        {
            return true;
        }

        StatusText.Text = "skin type is not valid.";
        return false;
    }

    private bool TryReadPositiveInt(TextBox box, string label, out int value)
    {
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            return true;
        }

        StatusText.Text = $"{label} must be greater than zero.";
        box.Focus();
        return false;
    }
}
