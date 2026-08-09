using System;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Activation-local admission barrier for callbacks owned by a plugin. Closing the gate rejects
/// callbacks that have not started yet, while leases already issued are allowed to finish. The
/// gate deliberately does not promise callback quiescence: lifecycle cleanup is scope-owned and
/// asynchronous operations coordinate their own bounded shutdown without blocking game updates.
/// </summary>
internal sealed class ActivationCallbackGate
{
    private int closed;

    internal bool IsClosed => Volatile.Read(ref closed) != 0;

    internal bool TryEnter(out Lease lease)
    {
        if (Volatile.Read(ref closed) != 0)
        {
            lease = default;
            return false;
        }

        if (Volatile.Read(ref closed) == 0)
        {
            lease = default;
            return true;
        }

        lease = default;
        return false;
    }

    internal void CloseAdmission()
    {
        Interlocked.Exchange(ref closed, 1);
    }

    internal readonly struct Lease : IDisposable
    {
        public void Dispose()
        {
            // A lease marks only that admission succeeded. Callback completion is intentionally
            // not counted here because no lifecycle path waits synchronously for arbitrary plugin
            // callbacks; stale work is prevented by closing admission before scope release.
        }
    }
}

/// <summary>Internal context capability used only by lifecycle coordination to close an activation.</summary>
internal interface IActivationCallbackAdmissionContext
{
    void CloseCallbackAdmission();
}

internal static class ActivationCallbackGates
{
    internal static ActivationCallbackGate? TryGet(IPluginResourceScope resources)
    {
        return resources is PluginResourceScope scope ? scope.CallbackGate : null;
    }
}
