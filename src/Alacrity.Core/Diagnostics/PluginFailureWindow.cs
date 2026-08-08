using System;

namespace Alacrity.Core;

/// <summary>Shared rolling failure state for isolated plugin render registrations.</summary>
internal sealed class PluginFailureWindow
{
    private readonly object gate = new object();
    private int count;
    private DateTime windowStartedUtc;
    private DateTime retryAtUtc;
    private State state;

    internal bool CanInvoke(DateTime now)
    {
        lock (gate)
        {
            if (state == State.Normal) return true;
            if (state == State.Trial) return false;
            if (now < retryAtUtc) return false;
            state = State.Trial;
            return true;
        }
    }

    internal void RecordFailure(DateTime now, TimeSpan window)
    {
        lock (gate)
        {
            if (state == State.Trial)
            {
                state = State.SuspendedUntil;
                retryAtUtc = now + window;
                return;
            }
            if (windowStartedUtc == default || now - windowStartedUtc > window) { windowStartedUtc = now; count = 1; }
            else count++;
            if (count >= 3)
            {
                state = State.SuspendedUntil;
                retryAtUtc = now + window;
            }
        }
    }

    internal void RecordSuccess()
    {
        lock (gate) { count = 0; windowStartedUtc = default; retryAtUtc = default; state = State.Normal; }
    }

    private enum State { Normal, SuspendedUntil, Trial }
}
