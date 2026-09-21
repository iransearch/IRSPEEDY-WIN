using System.Diagnostics;

namespace IRSpeedy.Hotspot;

// Only fixed stage names and numeric durations are recorded, never request/adapter data.
public sealed record StartupTiming(string Stage, long ElapsedMs, long TotalMs, bool Success);

internal sealed class StartupTimings
{
    private readonly Stopwatch clock = new();
    private readonly List<StartupTiming> values = new();
    internal StartupTiming[] Snapshot => values.ToArray();
    internal void Restart() { values.Clear(); clock.Restart(); }

    internal void Measure(string stage, Action action) =>
        Measure(stage, () => { action(); return true; });

    internal T Measure<T>(string stage, Func<T> action)
    {
        var elapsed = Stopwatch.StartNew();
        bool success = false;
        try { var result = action(); success = true; return result; }
        finally { Record(stage, elapsed, success); }
    }

    internal async Task MeasureAsync(string stage, Func<Task> action)
    {
        await MeasureAsync(stage, async () => { await action(); return true; });
    }

    internal async Task<T> MeasureAsync<T>(string stage, Func<Task<T>> action)
    {
        var elapsed = Stopwatch.StartNew();
        bool success = false;
        try { var result = await action(); success = true; return result; }
        finally { Record(stage, elapsed, success); }
    }

    private void Record(string stage, Stopwatch elapsed, bool success)
    {
        if (values.Count < 32)
            values.Add(new StartupTiming(stage, elapsed.ElapsedMilliseconds, clock.ElapsedMilliseconds, success));
    }
}
