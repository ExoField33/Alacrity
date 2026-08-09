using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>
/// Composes activation-owned requests for conservative world-render culling. The Terraria adapter
/// decides the actual bounds for each renderer; removing every registration always restores vanilla.
/// </summary>
public sealed class PluginRenderCullingHost
{
    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private PluginRenderCullingCategory effectiveCategories;

    public IPluginRenderCullingService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));

        if ((manifest.Capabilities & PluginCapability.Rendering) == 0 ||
            (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
        {
            return new DeniedService(manifest.Id);
        }

        var guard = new ScopeGuard();
        try
        {
            resources.Own("render-culling", PluginResourceKind.RenderingHandler, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }

        return new ScopedService(this, resources, guard);
    }

    public PluginRenderCullingCategory GetEffectiveCategories()
    {
        lock (gate)
        {
            return effectiveCategories;
        }
    }

    private IPluginRegistration Register(IPluginResourceScope resources, PluginRenderCullingPolicy policy)
    {
        if (policy == null) throw new ArgumentNullException(nameof(policy));

        PluginRenderCullingCategory categories = policy.Categories &
            (PluginRenderCullingCategory.Players |
             PluginRenderCullingCategory.DroppedItems |
             PluginRenderCullingCategory.Dust |
             PluginRenderCullingCategory.WorldParticles);
        var entry = new Entry(categories, Remove);

        try
        {
            resources.Own("render-culling-policy", PluginResourceKind.RenderingHandler, entry);
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
                throw new ObjectDisposedException("IPluginResourceScope", "The owning plugin scope was released during render-culling registration.");
            }

            entries.Add(entry);
            RebuildEffectiveCategories();
        }

        return entry;
    }

    private void Remove(Entry entry)
    {
        lock (gate)
        {
            if (!entries.Remove(entry)) return;
            RebuildEffectiveCategories();
        }
    }

    private void RebuildEffectiveCategories()
    {
        PluginRenderCullingCategory categories = PluginRenderCullingCategory.None;
        for (int index = 0; index < entries.Count; index++)
        {
            categories |= entries[index].Categories;
        }

        effectiveCategories = categories;
    }

    private sealed class ScopedService : IPluginRenderCullingService
    {
        private readonly PluginRenderCullingHost host;
        private readonly IPluginResourceScope resources;
        private readonly ScopeGuard guard;

        internal ScopedService(PluginRenderCullingHost host, IPluginResourceScope resources, ScopeGuard guard)
        {
            this.host = host;
            this.resources = resources;
            this.guard = guard;
        }

        public IPluginRegistration RegisterPolicy(PluginRenderCullingPolicy policy)
        {
            if (guard.IsReleased)
            {
                throw new ObjectDisposedException("IPluginRenderCullingService", "The owning plugin scope has been released.");
            }

            return host.Register(resources, policy);
        }
    }

    private sealed class DeniedService : IPluginRenderCullingService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public IPluginRegistration RegisterPolicy(PluginRenderCullingPolicy policy)
        {
            throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare Rendering capability and DrawUserInterface permission before registering render-culling policies.");
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;

        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            System.Threading.Interlocked.Exchange(ref released, 1);
        }
    }

    private sealed class Entry : IPluginRegistration
    {
        private readonly Action<Entry> remove;
        private int released;

        internal Entry(PluginRenderCullingCategory categories, Action<Entry> remove)
        {
            Categories = categories;
            this.remove = remove;
        }

        internal PluginRenderCullingCategory Categories { get; }
        public string Name => "render-culling-policy";
        public bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref released, 1) != 0) return;
            remove(this);
        }
    }
}
