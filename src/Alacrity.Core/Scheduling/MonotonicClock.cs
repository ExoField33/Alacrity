using System;
using System.Diagnostics;

namespace Alacrity.Core;

/// <summary>Minimal monotonic clock seam used by scheduler deadlines and deterministic tests.</summary>
internal interface IMonotonicClock
{
    long Frequency { get; }
    long GetTimestamp();
}

internal sealed class StopwatchMonotonicClock : IMonotonicClock
{
    internal static readonly StopwatchMonotonicClock Instance = new StopwatchMonotonicClock();

    private StopwatchMonotonicClock() { }

    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();
}

/// <summary>Frequency-safe conversion and deadline arithmetic for elapsed scheduler work.</summary>
internal static class MonotonicClockMath
{
    internal static long ToClockTicks(TimeSpan duration, long frequency)
    {
        if (duration <= TimeSpan.Zero) return 0;
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));

        long wholeSeconds = duration.Ticks / TimeSpan.TicksPerSecond;
        long remainder = duration.Ticks % TimeSpan.TicksPerSecond;
        long wholeTicks = SaturatingMultiply(wholeSeconds, frequency);
        long fractionalTicks;
        try
        {
            fractionalTicks = decimal.ToInt64(decimal.Ceiling((decimal)remainder * frequency / TimeSpan.TicksPerSecond));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }

        return SaturatingAdd(wholeTicks, fractionalTicks);
    }

    internal static long SaturatingAdd(long timestamp, long delay)
    {
        if (delay <= 0) return timestamp;
        return timestamp >= long.MaxValue - delay ? long.MaxValue : timestamp + delay;
    }

    private static long SaturatingMultiply(long left, long right)
    {
        if (left == 0 || right == 0) return 0;
        return left > long.MaxValue / right ? long.MaxValue : left * right;
    }
}
