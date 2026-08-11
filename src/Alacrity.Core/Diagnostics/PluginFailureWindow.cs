using System;
using System.Threading;

namespace Alacrity.Core;

/// <summary>Shared rolling failure state for isolated plugin render registrations.</summary>
internal sealed class PluginFailureWindow
{
    private readonly object gate = new object();
    private int count;
    private DateTime windowStartedUtc;
    private DateTime retryAtUtc;
    private int state;

    /// <summary>
    /// Keeps the normal render path clock-free. The caller only supplies a clock when this entry
    /// has failed before and may need to decide whether its retry cooldown has elapsed.
    /// </summary>
    internal bool CanInvoke(Func<DateTime> utcNow, out DateTime now)
    {
        if ((State)Volatile.Read(ref state) == State.Normal)
        {
            now = default;
            return true;
        }

        now = utcNow();
        lock (gate)
        {
            if ((State)state == State.Normal) return true;
            if ((State)state == State.Trial) return false;
            if (now < retryAtUtc) return false;
            Volatile.Write(ref state, (int)State.Trial);
            return true;
        }
    }

    internal void RecordFailure(DateTime now, TimeSpan window)
    {
        lock (gate)
        {
            if ((State)state == State.Trial)
            {
                Volatile.Write(ref state, (int)State.SuspendedUntil);
                retryAtUtc = now + window;
                return;
            }
            if (windowStartedUtc == default || now - windowStartedUtc > window) { windowStartedUtc = now; count = 1; }
            else count++;
            if (count >= 3)
            {
                Volatile.Write(ref state, (int)State.SuspendedUntil);
                retryAtUtc = now + window;
            }
        }
    }

    internal void RecordSuccess()
    {
        if ((State)Volatile.Read(ref state) == State.Normal && Volatile.Read(ref count) == 0)
        {
            return;
        }

        lock (gate)
        {
            count = 0;
            windowStartedUtc = default;
            retryAtUtc = default;
            Volatile.Write(ref state, (int)State.Normal);
        }
    }

    private enum State { Normal, SuspendedUntil, Trial }
}
