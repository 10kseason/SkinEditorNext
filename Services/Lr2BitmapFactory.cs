using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkinEditorNext.Models;

namespace SkinEditorNext.Services;

public sealed class Lr2BitmapFactory
{
    private readonly Dictionary<string, BitmapSource?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource?> _frameCache = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        _imageCache.Clear();
        _frameCache.Clear();
    }

    public bool TryCreateFrameBitmap(Lr2PreviewItem item, out BitmapSource bitmap)
    {
        bitmap = null!;
        var skinObject = item.Object;
        var source = TryLoadBitmap(skinObject.ImagePath);
        if (source is null) return false;

        var cacheKey = string.Join("|",
            skinObject.ImagePath,
            skinObject.SourceX.ToString(CultureInfo.InvariantCulture),
            skinObject.SourceY.ToString(CultureInfo.InvariantCulture),
            skinObject.SourceWidth.ToString(CultureInfo.InvariantCulture),
            skinObject.SourceHeight.ToString(CultureInfo.InvariantCulture),
            skinObject.SourceDivX.ToString(CultureInfo.InvariantCulture),
            skinObject.SourceDivY.ToString(CultureInfo.InvariantCulture),
            item.SourceFrame.ToString(CultureInfo.InvariantCulture),
            item.Red.ToString(CultureInfo.InvariantCulture),
            item.Green.ToString(CultureInfo.InvariantCulture),
            item.Blue.ToString(CultureInfo.InvariantCulture));

        if (_frameCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached is null) return false;
            bitmap = cached;
            return true;
        }

        if (!TryCreateCrop(source, skinObject, item.SourceFrame, out var crop))
        {
            _frameCache[cacheKey] = null;
            return false;
        }

        bitmap = ApplyBrightness(crop, item.Red, item.Green, item.Blue);
        bitmap.Freeze();
        _frameCache[cacheKey] = bitmap;
        return true;
    }

    private BitmapSource? TryLoadBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('*') || path.Equals("CONTINUE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_imageCache.TryGetValue(path, out var cached)) return cached;

        try
        {
            if (!File.Exists(path))
            {
                _imageCache[path] = null;
                return null;
            }

            BitmapSource? image = string.Equals(Path.GetExtension(path), ".tga", StringComparison.OrdinalIgnoreCase)
                ? TryLoadTga(path)
                : LoadWpfBitmap(path);

            image?.Freeze();
            _imageCache[path] = image;
            return image;
        }
        catch
        {
            _imageCache[path] = null;
            return null;
        }
    }

    private static BitmapSource LoadWpfBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        return image;
    }

    private static bool TryCreateCrop(BitmapSource bitmap, SkinObjectView item, int sourceFrame, out BitmapSource crop)
    {
        crop = bitmap;
        if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return false;

        var divX = Math.Max(1, item.SourceDivX);
        var divY = Math.Max(1, item.SourceDivY);
        var x = Math.Clamp(item.SourceX, 0, Math.Max(0, bitmap.PixelWidth - 1));
        var y = Math.Clamp(item.SourceY, 0, Math.Max(0, bitmap.PixelHeight - 1));
        var fullWidth = item.SourceWidth <= 0 ? bitmap.PixelWidth - x : item.SourceWidth;
        var fullHeight = item.SourceHeight <= 0 ? bitmap.PixelHeight - y : item.SourceHeight;
        var frameX = Math.Clamp(sourceFrame % divX, 0, divX - 1);
        var frameY = Math.Clamp(sourceFrame / divX, 0, divY - 1);
        var width = Math.Max(1, fullWidth / divX);
        var height = Math.Max(1, fullHeight / divY);
        x += width * frameX;
        y += height * frameY;
        x = Math.Clamp(x, 0, Math.Max(0, bitmap.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, bitmap.PixelHeight - 1));
        width = Math.Clamp(width, 1, Math.Max(1, bitmap.PixelWidth - x));
        height = Math.Clamp(height, 1, Math.Max(1, bitmap.PixelHeight - y));

        crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, width, height));
        crop.Freeze();
        return true;
    }

    private static BitmapSource ApplyBrightness(BitmapSource source, int red, int green, int blue)
    {
        red = Math.Clamp(red, 0, 255);
        green = Math.Clamp(green, 0, 255);
        blue = Math.Clamp(blue, 0, 255);

        if (red == 255 && green == 255 && blue == 255)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(pixels[i] * blue / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * green / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * red / 255);
        }

        return BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
    }

    private static BitmapSource? TryLoadTga(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 18) return null;

        var idLength = data[0];
        var colorMapType = data[1];
        var imageType = data[2];
        if (colorMapType != 0 || imageType is not (2 or 10)) return null;

        var width = ReadUInt16(data, 12);
        var height = ReadUInt16(data, 14);
        var bitsPerPixel = data[16];
        var descriptor = data[17];
        if (width <= 0 || height <= 0 || bitsPerPixel is not (24 or 32)) return null;

        var bytesPerPixel = bitsPerPixel / 8;
        var sourceOffset = 18 + idLength;
        var pixels = new byte[width * height * 4];
        var topOrigin = (descriptor & 0x20) != 0;

        if (imageType == 2)
        {
            var sourceIndex = sourceOffset;
            for (var row = 0; row < height; row++)
            {
                var targetRow = topOrigin ? row : height - 1 - row;
                for (var column = 0; column < width; column++)
                {
                    if (sourceIndex + bytesPerPixel > data.Length) return null;
                    CopyTgaPixel(data, sourceIndex, pixels, (targetRow * width + column) * 4, bytesPerPixel);
                    sourceIndex += bytesPerPixel;
                }
            }
        }
        else
        {
            var sourceIndex = sourceOffset;
            var pixelIndex = 0;
            while (pixelIndex < width * height)
            {
                if (sourceIndex >= data.Length) return null;
                var packet = data[sourceIndex++];
                var runLength = (packet & 0x7f) + 1;
                var isRle = (packet & 0x80) != 0;

                if (isRle)
                {
                    if (sourceIndex + bytesPerPixel > data.Length) return null;
                    for (var i = 0; i < runLength && pixelIndex < width * height; i++)
                    {
                        CopyTgaPixelToLinear(data, sourceIndex, pixels, pixelIndex++, width, height, topOrigin, bytesPerPixel);
                    }
                    sourceIndex += bytesPerPixel;
                }
                else
                {
                    for (var i = 0; i < runLength && pixelIndex < width * height; i++)
                    {
                        if (sourceIndex + bytesPerPixel > data.Length) return null;
                        CopyTgaPixelToLinear(data, sourceIndex, pixels, pixelIndex++, width, height, topOrigin, bytesPerPixel);
                        sourceIndex += bytesPerPixel;
                    }
                }
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return data[offset] | (data[offset + 1] << 8);
    }

    private static void CopyTgaPixelToLinear(
        byte[] source,
        int sourceOffset,
        byte[] target,
        int pixelIndex,
        int width,
        int height,
        bool topOrigin,
        int bytesPerPixel)
    {
        var row = pixelIndex / width;
        var column = pixelIndex % width;
        var targetRow = topOrigin ? row : height - 1 - row;
        CopyTgaPixel(source, sourceOffset, target, (targetRow * width + column) * 4, bytesPerPixel);
    }

    private static void CopyTgaPixel(byte[] source, int sourceOffset, byte[] target, int targetOffset, int bytesPerPixel)
    {
        target[targetOffset] = source[sourceOffset];
        target[targetOffset + 1] = source[sourceOffset + 1];
        target[targetOffset + 2] = source[sourceOffset + 2];
        target[targetOffset + 3] = bytesPerPixel == 4 ? source[sourceOffset + 3] : (byte)255;
    }
}
