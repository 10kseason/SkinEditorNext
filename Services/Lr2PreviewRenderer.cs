using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SkinEditorNext.Services;

public sealed class Lr2PreviewRenderer
{
    private readonly Lr2PreviewEvaluator _evaluator = new();
    private readonly Lr2BitmapFactory _bitmapFactory = new();

    public RenderSummary RenderToPng(Lr2SkinDocument document, double timeMs, string outputPath)
    {
        return RenderToPng(document, Lr2PreviewClock.Create(timeMs, "playing"), outputPath);
    }

    public RenderSummary RenderToPng(Lr2SkinDocument document, Lr2PreviewClock clock, string outputPath)
    {
        var resolution = document.Resolution.IsValid ? document.Resolution : Models.ResolutionInfo.Default;
        var renderItems = _evaluator.Evaluate(document, clock, maxItems: 5000);
        var drawingVisual = new DrawingVisual();
        var loadedImages = 0;
        var placeholderImages = 0;

        using (var dc = drawingVisual.RenderOpen())
        {
            var bounds = new Rect(0, 0, resolution.Width, resolution.Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 24, 39)), null, bounds);

            foreach (var item in renderItems)
            {
                var dest = new Rect(item.Destination.X, item.Destination.Y, item.Destination.Width, item.Destination.Height);
                if (_bitmapFactory.TryCreateFrameBitmap(item, out var bitmap))
                {
                    loadedImages++;
                    PushRenderState(dc, item, dest);
                    dc.DrawImage(bitmap, dest);
                    PopRenderState(dc, item);
                    continue;
                }

                placeholderImages++;
                PushRenderState(dc, item, dest);
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                    new Pen(new SolidColorBrush(Color.FromRgb(250, 204, 21)), 1),
                    dest);
                PopRenderState(dc, item);
            }

            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(229, 231, 235)), 1), bounds);
        }

        var bitmapTarget = new RenderTargetBitmap(
            resolution.Width,
            resolution.Height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmapTarget.Render(drawingVisual);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapTarget));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);

        return new RenderSummary(clock.ModeName, renderItems.Count, loadedImages, placeholderImages, outputPath);
    }

    private static void PushRenderState(DrawingContext dc, Lr2PreviewItem item, Rect dest)
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

    private static void PopRenderState(DrawingContext dc, Lr2PreviewItem item)
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
}

public sealed record RenderSummary(string ModeName, int VisibleItems, int LoadedImages, int PlaceholderImages, string OutputPath);
