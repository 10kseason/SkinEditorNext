namespace SkinEditorNext.Services;

public sealed class Lr2PreviewClock
{
    private readonly HashSet<int>? _activeTimers;
    private readonly Dictionary<int, double> _timerOffsets;

    private Lr2PreviewClock(double timeMs, HashSet<int>? activeTimers, string modeName, Dictionary<int, double>? timerOffsets = null)
    {
        TimeMs = timeMs;
        _activeTimers = activeTimers;
        ModeName = modeName;
        _timerOffsets = timerOffsets ?? [];
    }

    public double TimeMs { get; }
    public string ModeName { get; }

    public static Lr2PreviewClock Create(double timeMs, string? mode)
    {
        return (mode ?? "playing").Trim().ToLowerInvariant() switch
        {
            "all" or "alltimers" or "all-timers" => new Lr2PreviewClock(timeMs, null, "All timers"),
            "ready" => new Lr2PreviewClock(timeMs, Timers(0, 40, 140), "Ready"),
            "scene" or "scenestart" or "scene-start" => new Lr2PreviewClock(timeMs, Timers(0, 140), "Scene"),
            _ => new Lr2PreviewClock(timeMs, Timers(0, 40, 41, 140), "Playing", new Dictionary<int, double> { [40] = 4000 })
        };
    }

    public double GetTimeLapse(int timerId)
    {
        if (timerId == 140)
        {
            return TimeMs % 1000.0;
        }

        if (timerId < 0 || timerId > 500)
        {
            return -1.0;
        }

        if (_activeTimers is null || _activeTimers.Contains(timerId))
        {
            return Math.Max(0.0, TimeMs + (_timerOffsets.TryGetValue(timerId, out var offset) ? offset : 0.0));
        }

        return -1.0;
    }

    private static HashSet<int> Timers(params int[] timers)
    {
        return new HashSet<int>(timers);
    }
}
