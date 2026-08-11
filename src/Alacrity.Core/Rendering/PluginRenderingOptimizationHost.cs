using System;
using System.Collections.Generic;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Composes activation-owned, host-implemented rendering optimization requests. The effective
/// value is published atomically so version-locked rendering gates never enumerate plugins.
/// </summary>
public sealed class PluginRenderingOptimizationHost
{
    private const PluginRenderingOptimization SupportedOptimizations =
        PluginRenderingOptimization.PaintedTilePreparation |
        PluginRenderingOptimization.ClothingEntityPresentation |
        PluginRenderingOptimization.WaterfallPresentation |
        PluginRenderingOptimization.TileDrawingPresentation |
        PluginRenderingOptimization.DrawOrchestration |
        PluginRenderingOptimization.LaserRulerPresentation;

    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private int effectiveOptimizations;

    public IPluginRenderingOptimizationService CreateService(PluginManifest manifest, IPluginResourceScope resources)
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
            resources.Own("rendering-optimizations", PluginResourceKind.RenderingHandler, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }

        return new ScopedService(this, resources, guard);
    }

    public PluginRenderingOptimization GetEffectiveOptimizations()
    {
        return (PluginRenderingOptimization)Volatile.Read(ref effectiveOptimizations);
    }

    private IPluginRegistration Register(IPluginResourceScope resources, PluginRenderingOptimizationPolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        var entry = new Entry(policy.Optimizations & SupportedOptimizations, Remove);
        try
        {
            resources.Own("rendering-optimization-policy", PluginResourceKind.RenderingHandler, entry);
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
                throw new ObjectDisposedException("IPluginResourceScope", "The owning plugin scope was released during rendering-optimization registration.");
            }

            entries.Add(entry);
            RebuildEffectiveOptimizations();
        }

        return entry;
    }

    private void Remove(Entry entry)
    {
        lock (gate)
        {
            if (!entries.Remove(entry))
            {
                return;
            }

            RebuildEffectiveOptimizations();
        }
    }

    private void RebuildEffectiveOptimizations()
    {
        PluginRenderingOptimization optimizations = PluginRenderingOptimization.None;
        for (int index = 0; index < entries.Count; index++)
        {
            optimizations |= entries[index].Optimizations;
        }

        Volatile.Write(ref effectiveOptimizations, (int)optimizations);
    }

    private sealed class ScopedService : IPluginRenderingOptimizationService
    {
        private readonly PluginRenderingOptimizationHost host;
        private readonly IPluginResourceScope resources;
        private readonly ScopeGuard guard;

        internal ScopedService(PluginRenderingOptimizationHost host, IPluginResourceScope resources, ScopeGuard guard)
        {
            this.host = host;
            this.resources = resources;
            this.guard = guard;
        }

        public IPluginRegistration RegisterPolicy(PluginRenderingOptimizationPolicy policy)
        {
            if (guard.IsReleased)
            {
                throw new ObjectDisposedException("IPluginRenderingOptimizationService", "The owning plugin scope has been released.");
            }

            return host.Register(resources, policy);
        }
    }

    private sealed class DeniedService : IPluginRenderingOptimizationService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public IPluginRegistration RegisterPolicy(PluginRenderingOptimizationPolicy policy)
        {
            throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare the Rendering capability before registering rendering optimizations.");
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

        internal Entry(PluginRenderingOptimization optimizations, Action<Entry> remove)
        {
            Optimizations = optimizations;
            this.remove = remove;
        }

        internal PluginRenderingOptimization Optimizations { get; }
        public string Name => "rendering-optimization-policy";
        public bool IsReleased => Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            remove(this);
        }
    }
}
