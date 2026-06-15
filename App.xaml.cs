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
}
