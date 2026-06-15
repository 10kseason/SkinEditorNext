using System.IO;
using System.Windows.Media.Imaging;

namespace SkinEditorNext.Services;

public static class Lr2ImageProbe
{
    public static bool TryGetSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

        try
        {
            if (string.Equals(Path.GetExtension(path), ".tga", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetTgaSize(path, out width, out height);
            }

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            return width > 0 && height > 0;
        }
        catch
        {
            width = 0;
            height = 0;
            return false;
        }
    }

    private static bool TryGetTgaSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        Span<byte> header = stackalloc byte[18];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length) return false;

        var colorMapType = header[1];
        var imageType = header[2];
        if (colorMapType != 0 || imageType is not (2 or 10)) return false;

        width = header[12] | (header[13] << 8);
        height = header[14] | (header[15] << 8);
        var bitsPerPixel = header[16];
        return width > 0 && height > 0 && bitsPerPixel is 24 or 32;
    }
}
