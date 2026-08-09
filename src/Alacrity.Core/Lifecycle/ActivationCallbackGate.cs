using System;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Activation-local admission barrier for callbacks owned by a plugin. Closing the gate rejects
/// callbacks that have not started yet, while leases already issued are allowed to finish.
/// </summary>
internal sealed class ActivationCallbackGate
{
    private int closed;
    private int activeCallbacks;

    internal bool IsClosed => Volatile.Read(ref closed) != 0;

    internal int ActiveCallbacks => Volatile.Read(ref activeCallbacks);

    internal bool TryEnter(out Lease lease)
    {
        if (Volatile.Read(ref closed) != 0)
        {
            lease = default;
            return false;
        }

        Interlocked.Increment(ref activeCallbacks);
        if (Volatile.Read(ref closed) == 0)
        {
            lease = new Lease(this);
            return true;
        }

        Interlocked.Decrement(ref activeCallbacks);
        lease = default;
        return false;
    }

    internal void CloseAdmission()
    {
        Interlocked.Exchange(ref closed, 1);
    }

    internal readonly struct Lease : IDisposable
    {
        private readonly ActivationCallbackGate? owner;

        internal Lease(ActivationCallbackGate owner)
        {
            this.owner = owner;
        }

        public void Dispose()
        {
            if (owner != null)
            {
                Interlocked.Decrement(ref owner.activeCallbacks);
            }
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
