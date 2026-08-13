using System;
using System.Collections.Generic;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Composes activation-owned local presentation suppression requests. The effective mask is
/// atomically published so version-locked render gates do not enumerate plugin registrations.
/// </summary>
public sealed class PluginPresentationSuppressionHost
{
    private const PluginPresentationElement SupportedElements = PluginPresentationElement.PaladinShieldIcon;

    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private int effectiveElements;

    public IPluginPresentationSuppressionService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));

        if ((manifest.Capabilities & PluginCapability.Rendering) == 0)
        {
            return new DeniedService(manifest.Id);
        }

        var guard = new ScopeGuard();
        try
        {
            resources.Own("presentation-suppression", PluginResourceKind.RenderingHandler, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }

        return new ScopedService(this, resources, guard);
    }

    /// <summary>Returns the atomically published local presentation suppression mask.</summary>
    public PluginPresentationElement GetEffectiveElements()
    {
        return (PluginPresentationElement)Volatile.Read(ref effectiveElements);
    }

    private IPluginRegistration Register(IPluginResourceScope resources, PluginPresentationSuppressionPolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var entry = new Entry(policy.Elements & SupportedElements, Remove);
        try
        {
            // Own before publishing so a released activation can never become observable.
            resources.Own("presentation-suppression-policy", PluginResourceKind.RenderingHandler, entry);
        }
        catch
        {
            entry.Dispose();
            throw;
        }

        lock (gate)
        {
            if (entry.IsReleased)
            {
                throw new ObjectDisposedException(
                    "IPluginResourceScope",
                    "The owning plugin scope was released during presentation-suppression registration.");
            }

            entries.Add(entry);
            RebuildEffectiveElements();
        }

        return entry;
    }

    private void Remove(Entry entry)
    {
        lock (gate)
        {
            if (!entries.Remove(entry)) return;
            RebuildEffectiveElements();
        }
    }

    private void RebuildEffectiveElements()
    {
        PluginPresentationElement elements = PluginPresentationElement.None;
        for (int index = 0; index < entries.Count; index++)
        {
            elements |= entries[index].Elements;
        }

        Volatile.Write(ref effectiveElements, (int)elements);
    }

    private sealed class ScopedService : IPluginPresentationSuppressionService
    {
        private readonly PluginPresentationSuppressionHost host;
        private readonly IPluginResourceScope resources;
        private readonly ScopeGuard guard;

        internal ScopedService(PluginPresentationSuppressionHost host, IPluginResourceScope resources, ScopeGuard guard)
        {
            this.host = host;
            this.resources = resources;
            this.guard = guard;
        }

        public IPluginRegistration RegisterPolicy(PluginPresentationSuppressionPolicy policy)
        {
            if (guard.IsReleased)
            {
                throw new ObjectDisposedException(
                    "IPluginPresentationSuppressionService",
                    "The owning plugin scope has been released.");
            }

            return host.Register(resources, policy);
        }
    }

    private sealed class DeniedService : IPluginPresentationSuppressionService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public IPluginRegistration RegisterPolicy(PluginPresentationSuppressionPolicy policy)
        {
            throw new UnauthorizedAccessException(
                "Plugin '" + owner.Value + "' must declare the Rendering capability before suppressing local presentation.");
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;

        internal bool IsReleased => Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            Interlocked.Exchange(ref released, 1);
        }
    }

    private sealed class Entry : IPluginRegistration
    {
        private readonly Action<Entry> remove;
        private int released;

        internal Entry(PluginPresentationElement elements, Action<Entry> remove)
        {
            Elements = elements;
            this.remove = remove;
        }

        internal PluginPresentationElement Elements { get; }
        public string Name => "presentation-suppression-policy";
        public bool IsReleased => Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            remove(this);
        }
    }
}
