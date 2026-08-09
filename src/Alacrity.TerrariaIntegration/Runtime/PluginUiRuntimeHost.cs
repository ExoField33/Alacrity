using System;
using System.Threading;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Owns process-wide runtime admission state behind the version-locked bridge facade.
/// The facade may forward calls from Terraria at any time, but only this host publishes
/// the managed runtime instance or admits a bootstrap/shutdown transition.
/// </summary>
internal sealed class PluginUiRuntimeHost
{
    private readonly object gate = new object();
    private PluginUiRuntimeState state;
    private int bootstrapped;
    private int bootstrapInProgress;
    private int shuttingDown;

    internal PluginUiRuntimeState State => Volatile.Read(ref state);

    internal bool IsBootstrapped => Volatile.Read(ref bootstrapped) != 0;

    internal bool IsShuttingDown => Volatile.Read(ref shuttingDown) != 0;

    internal void SetState(PluginUiRuntimeState value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        lock (gate)
        {
            if (state != null)
            {
                return;
            }

            state = value;
        }
    }

    internal bool TryBeginBootstrap()
    {
        if (IsBootstrapped || IsShuttingDown)
        {
            return false;
        }

        lock (gate)
        {
            if (IsBootstrapped || IsShuttingDown || bootstrapInProgress != 0)
            {
                return false;
            }

            bootstrapInProgress = 1;
            return true;
        }
    }

    internal void CompleteBootstrap()
    {
        Volatile.Write(ref bootstrapped, 1);
    }

    internal void EndBootstrap()
    {
        lock (gate)
        {
            bootstrapInProgress = 0;
        }
    }

    internal bool TryBeginShutdown()
    {
        if (IsShuttingDown)
        {
            return false;
        }

        lock (gate)
        {
            if (IsShuttingDown)
            {
                return false;
            }

            Volatile.Write(ref shuttingDown, 1);
            return true;
        }
    }
}
