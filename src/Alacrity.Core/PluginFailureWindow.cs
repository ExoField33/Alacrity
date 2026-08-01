using System;

namespace Alacrity.Core;

/// <summary>Shared rolling failure state for isolated plugin render registrations.</summary>
internal sealed class PluginFailureWindow
{
    private readonly object gate = new object();
    private int count;
    private DateTime windowStartedUtc;
    private DateTime retryAtUtc;

    internal bool CanInvoke(DateTime now)
    {
        lock (gate) return count < 3 || now >= retryAtUtc;
    }

    internal void RecordFailure(DateTime now, TimeSpan window)
    {
        lock (gate)
        {
            if (windowStartedUtc == default || now - windowStartedUtc > window) { windowStartedUtc = now; count = 1; }
            else count++;
            if (count >= 3) retryAtUtc = now + window;
        }
    }

    internal void RecordSuccess()
    {
        lock (gate) { count = 0; windowStartedUtc = default; retryAtUtc = default; }
    }
}
