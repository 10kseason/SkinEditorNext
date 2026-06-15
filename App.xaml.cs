using System.IO;
using System.Windows;
using SkinEditorNext.Services;

namespace SkinEditorNext;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--parse", StringComparison.OrdinalIgnoreCase))
        {
            RunParseSmoke(e.Args[1]);
            return;
        }

        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--render-info", StringComparison.OrdinalIgnoreCase))
        {
            var timeMs = e.Args.Length >= 3 && double.TryParse(e.Args[2], out var parsed) ? parsed : 1000;
            var mode = e.Args.Length >= 4 ? e.Args[3] : "playing";
            RunRenderInfoSmoke(e.Args[1], timeMs, mode);
            return;
        }

        if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--render-png", StringComparison.OrdinalIgnoreCase))
        {
            var timeMs = e.Args.Length >= 4 && double.TryParse(e.Args[2], out var parsed) ? parsed : 1000;
            var outputPath = e.Args.Length >= 4 ? e.Args[3] : e.Args[2];
            var mode = e.Args.Length >= 5 ? e.Args[4] : "playing";
            RunRenderPng(e.Args[1], timeMs, outputPath, mode);
            return;
        }

        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--create-skin", StringComparison.OrdinalIgnoreCase))
        {
            RunCreateSkin(e.Args);
            return;
        }

        if (e.Args.Length >= 3 && string.Equals(e.Args[0], "--create-skin-smoke", StringComparison.OrdinalIgnoreCase))
        {
            RunCreateSkinSmoke(e.Args[1], e.Args[2]);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    private void RunParseSmoke(string path)
    {
        try
        {
            var document = new Lr2SkinParser().Load(path);
            Console.WriteLine($"file={document.MainPath}");
            Console.WriteLine($"encoding={document.Encoding.WebName}");
            Console.WriteLine($"resolution={document.Resolution.Width}x{document.Resolution.Height}");
            Console.WriteLine($"lines={document.Lines.Count}");
            Console.WriteLine($"objects={document.Objects.Count}");
            Console.WriteLine($"diagnostics={document.Diagnostics.Count}");
            foreach (var diagnostic in document.Diagnostics.Take(10))
            {
                Console.WriteLine($"diagnostic={diagnostic}");
            }
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
    }

    private void RunRenderInfoSmoke(string path, double timeMs, string mode)
    {
        try
        {
            var document = new Lr2SkinParser().Load(path);
            var clock = Lr2PreviewClock.Create(timeMs, mode);
            var renderItems = new Lr2PreviewEvaluator().Evaluate(document, clock);
            Console.WriteLine($"file={document.MainPath}");
            Console.WriteLine($"resolution={document.Resolution.Width}x{document.Resolution.Height}");
            Console.WriteLine($"timeMs={(int)timeMs}");
            Console.WriteLine($"mode={clock.ModeName}");
            Console.WriteLine($"objects={document.Objects.Count}");
            Console.WriteLine($"visible={renderItems.Count}");
            Console.WriteLine($"diagnostics={document.Diagnostics.Count}");
            foreach (var item in renderItems.Take(10))
            {
                Console.WriteLine($"item={item.SortId},{item.Object.Kind},{item.Destination.X:0},{item.Destination.Y:0},{item.Destination.Width:0},{item.Destination.Height:0},{item.Object.ImagePath}");
            }
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
    }

    private void RunRenderPng(string path, double timeMs, string outputPath, string mode)
    {
        try
        {
            var document = new Lr2SkinParser().Load(path);
            var clock = Lr2PreviewClock.Create(timeMs, mode);
            var summary = new Lr2PreviewRenderer().RenderToPng(document, clock, outputPath);
            Console.WriteLine($"file={document.MainPath}");
            Console.WriteLine($"resolution={document.Resolution.Width}x{document.Resolution.Height}");
            Console.WriteLine($"timeMs={(int)timeMs}");
            Console.WriteLine($"mode={summary.ModeName}");
            Console.WriteLine($"objects={document.Objects.Count}");
            Console.WriteLine($"visible={summary.VisibleItems}");
            Console.WriteLine($"loadedImages={summary.LoadedImages}");
            Console.WriteLine($"placeholderImages={summary.PlaceholderImages}");
            Console.WriteLine($"diagnostics={document.Diagnostics.Count}");
            Console.WriteLine($"output={summary.OutputPath}");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
    }

    private void RunCreateSkin(string[] args)
    {
        try
        {
            var path = args[1];
            var type = args.Length >= 3 && int.TryParse(args[2], out var parsedType) ? parsedType : Lr2SkinWriter.DefaultSettings.SkinType;
            var width = args.Length >= 4 && int.TryParse(args[3], out var parsedWidth) ? parsedWidth : Lr2SkinWriter.DefaultSettings.Width;
            var height = args.Length >= 5 && int.TryParse(args[4], out var parsedHeight) ? parsedHeight : Lr2SkinWriter.DefaultSettings.Height;
            var title = args.Length >= 6 ? args[5] : Lr2SkinWriter.DefaultSettings.Title;
            var settings = new NewSkinSettings(type, title, string.Empty, string.Empty, width, height);

            WriteText(path, Lr2SkinWriter.CreateNewSkin(settings));
            Console.WriteLine($"created={path}");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
    }

    private void RunCreateSkinSmoke(string path, string imagePath)
    {
        try
        {
            var fullImagePath = Path.GetFullPath(imagePath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
            var relativeImagePath = Path.GetRelativePath(directory, fullImagePath);
            var text = Lr2SkinWriter.CreateNewSkin(Lr2SkinWriter.DefaultSettings);

            if (!Lr2ImageProbe.TryGetSize(fullImagePath, out var width, out var height))
            {
                width = 320;
                height = 180;
            }

            var dst = new Lr2DstSpec(0, 0, 0, width, height, 0, 255, 255, 255, 255, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var lines = new List<string>
            {
                string.Empty,
                "// SkinEditorNext smoke object",
                Lr2SkinWriter.ImageLine(relativeImagePath)
            };
            lines.AddRange(Lr2SkinWriter.ImageObjectLines(0, width, height, dst));

            WriteText(path, text + string.Join(Environment.NewLine, lines) + Environment.NewLine);
            Console.WriteLine($"created={path}");
            Console.WriteLine($"image={relativeImagePath}");
            Console.WriteLine($"size={width}x{height}");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Shutdown(1);
        }
    }

    private static void WriteText(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
