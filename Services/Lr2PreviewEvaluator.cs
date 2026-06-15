using SkinEditorNext.Models;

namespace SkinEditorNext.Services;

public sealed class Lr2PreviewEvaluator
{
    public IReadOnlyList<Lr2PreviewItem> Evaluate(Lr2SkinDocument document, double timeMs, int maxItems = 2000)
    {
        return Evaluate(document, Lr2PreviewClock.Create(timeMs, "playing"), maxItems);
    }

    public IReadOnlyList<Lr2PreviewItem> Evaluate(Lr2SkinDocument document, Lr2PreviewClock clock, int maxItems = 2000)
    {
        var result = new List<Lr2PreviewItem>();

        foreach (var item in document.Objects)
        {
            if (result.Count >= maxItems) break;
            if (item.Frames.Count == 0) continue;
            if (!OptionsMatch(item.DestOp1, item.DestOp2, item.DestOp3, item.DestOp4, item.DestOp5, document.ActiveOptions, clock))
            {
                continue;
            }

            var dstTime = clock.GetTimeLapse(item.DestTimer);
            var draw = EvaluateDst(item.Frames, item.DestLoop, dstTime);
            if (draw is null || draw.Width == 0 || draw.Height == 0)
            {
                continue;
            }

            if (draw.Blend != 0 && draw.Alpha < 2)
            {
                continue;
            }

            var sourceFrame = GetSourceFrameIndex(item, clock.GetTimeLapse(item.SourceTimer), dstTime);
            var destination = NormalizeDestination(draw.X, draw.Y, draw.Width, draw.Height);
            if (destination.Width <= 0 || destination.Height <= 0)
            {
                continue;
            }

            result.Add(new Lr2PreviewItem(
                item,
                destination,
                sourceFrame,
                draw.SortId,
                draw.Blend == 0 ? 1.0 : Math.Clamp(draw.Alpha / 255.0, 0.0, 1.0),
                Math.Clamp(draw.Red, 0, 255),
                Math.Clamp(draw.Green, 0, 255),
                Math.Clamp(draw.Blue, 0, 255),
                draw.Blend,
                draw.Filter,
                draw.Angle,
                draw.Center));
        }

        return result.OrderBy(item => item.SortId).ToList();
    }

    private static EvaluatedDst? EvaluateDst(IReadOnlyList<SkinDstFrame> frames, int loop, double timeMs)
    {
        var tStart = frames[0].Time;
        var tEnd = frames[^1].Time;
        var t = (int)timeMs;
        var t2 = tEnd;

        if (tStart > tEnd || tStart > t || (loop < 0 && t > tEnd))
        {
            return null;
        }

        if (tStart == tEnd || loop == tEnd)
        {
            if (t < t2) t2 = t;
        }
        else if (loop < tEnd)
        {
            t2 = t;
            if (tEnd < t)
            {
                t2 = (t - loop) % (tEnd - loop) + loop;
            }
        }
        else
        {
            t2 = 0;
        }

        var selected = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Time <= t2)
            {
                selected = i;
            }
        }

        var current = frames[selected];
        if (t2 != current.Time && selected != frames.Count - 1)
        {
            var next = frames[selected + 1];
            return new EvaluatedDst(
                ChangeValueByTime(current.X, next.X, current.Time, next.Time, t2, current.Acc),
                ChangeValueByTime(current.Y, next.Y, current.Time, next.Time, t2, current.Acc),
                ChangeValueByTime(current.Width, next.Width, current.Time, next.Time, t2, current.Acc),
                ChangeValueByTime(current.Height, next.Height, current.Time, next.Time, t2, current.Acc),
                current.Blend,
                current.Filter,
                (int)ChangeValueByTime(current.Alpha, next.Alpha, current.Time, next.Time, t2, current.Acc),
                (int)ChangeValueByTime(current.Red, next.Red, current.Time, next.Time, t2, current.Acc),
                (int)ChangeValueByTime(current.Green, next.Green, current.Time, next.Time, t2, current.Acc),
                (int)ChangeValueByTime(current.Blue, next.Blue, current.Time, next.Time, t2, current.Acc),
                ChangeValueByTime(current.Angle, next.Angle, current.Time, next.Time, t2, current.Acc),
                current.Center,
                (int)ChangeValueByTime(current.SortId, next.SortId, current.Time, next.Time, t2, current.Acc));
        }

        return new EvaluatedDst(
            current.X,
            current.Y,
            current.Width,
            current.Height,
            current.Blend,
            current.Filter,
            current.Alpha,
            current.Red,
            current.Green,
            current.Blue,
            current.Angle,
            current.Center,
            current.SortId);
    }

    private static double ChangeValueByTime(double value1, double value2, double time1, double time2, double timeNow, int type)
    {
        if (time1 == time2)
        {
            return value2;
        }

        if (timeNow <= time1)
        {
            return value1;
        }

        if (time2 <= timeNow)
        {
            return value2;
        }

        var ratio = (timeNow - time1) / (time2 - time1);
        return type switch
        {
            0 => value1 + (value2 - value1) * ratio,
            1 => value1 + (value2 - value1) * ratio * ratio * ratio,
            2 => value1 + (value2 - value1) * (1.0 - Math.Pow(1.0 - ratio, 3.0)),
            _ => value1
        };
    }

    private static int GetSourceFrameIndex(SkinObjectView item, double sourceTime, double dstTime)
    {
        var graphCount = Math.Max(1, item.SourceDivX * item.SourceDivY);
        if (item.SourceCycle <= 0 || sourceTime < 0)
        {
            return 0;
        }

        if (item.SourceTimer == item.DestTimer && item.Frames.Count > 0 && dstTime >= 0)
        {
            sourceTime -= item.Frames[0].Time;
        }

        if (sourceTime < 0)
        {
            return 0;
        }

        var frame = ((int)sourceTime % item.SourceCycle) * graphCount / item.SourceCycle;
        return frame < 0 || frame >= graphCount ? 0 : frame;
    }

    private static bool OptionsMatch(int op1, int op2, int op3, int op4, int op5, IReadOnlySet<int> activeOptions, Lr2PreviewClock clock)
    {
        return OptionMatches(op1, activeOptions, clock) &&
               OptionMatches(op2, activeOptions, clock) &&
               OptionMatches(op3, activeOptions, clock) &&
               OptionMatches(op4, activeOptions, clock) &&
               OptionMatches(op5, activeOptions, clock);
    }

    private static bool OptionMatches(int option, IReadOnlySet<int> activeOptions, Lr2PreviewClock clock)
    {
        if (option == 0) return true;

        var inverted = option < 0;
        var key = Math.Abs(option);

        if (key is >= 900 and <= 999)
        {
            var active = activeOptions.Contains(key);
            return inverted ? !active : active;
        }

        if (key == 80)
        {
            var active = clock.GetTimeLapse(40) < 0.0;
            return inverted ? !active : active;
        }

        if (key == 81)
        {
            var active = clock.GetTimeLapse(40) >= 0.0;
            return inverted ? !active : active;
        }

        return !inverted;
    }

    private static PreviewRect NormalizeDestination(double x, double y, double width, double height)
    {
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

        return new PreviewRect(x, y, width, height);
    }

    private sealed record EvaluatedDst(
        double X,
        double Y,
        double Width,
        double Height,
        int Blend,
        int Filter,
        int Alpha,
        int Red,
        int Green,
        int Blue,
        double Angle,
        int Center,
        int SortId);
}

public readonly record struct PreviewRect(double X, double Y, double Width, double Height);

public sealed record Lr2PreviewItem(
    SkinObjectView Object,
    PreviewRect Destination,
    int SourceFrame,
    int SortId,
    double Opacity,
    int Red,
    int Green,
    int Blue,
    int Blend,
    int Filter,
    double Angle,
    int Center);
