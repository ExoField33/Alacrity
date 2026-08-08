using System;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Coordinates a transient registration with the resource-scope wrapper that owns it.
/// Completion and scope cleanup may race attachment; exactly one path releases the wrapper.
/// </summary>
internal sealed class TransientRegistrationOwnership
{
    private readonly object gate = new object();
    private IPluginResourceHandle? ownership;
    private int released;

    internal bool IsReleased => Volatile.Read(ref released) != 0;

    internal void Attach(IPluginResourceHandle resource)
    {
        if (resource == null)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        bool releaseImmediately;
        lock (gate)
        {
            releaseImmediately = IsReleased;
            if (!releaseImmediately)
            {
                ownership = resource;
            }
        }

        if (releaseImmediately)
        {
            resource.Dispose();
        }
    }

    /// <summary>Marks the registration released and returns whether this caller won disposal.</summary>
    internal bool Release()
    {
        if (Interlocked.Exchange(ref released, 1) != 0)
        {
            return false;
        }

        IPluginResourceHandle? resource;
        lock (gate)
        {
            resource = ownership;
            ownership = null;
        }

        resource?.Dispose();
        return true;
    }
}
