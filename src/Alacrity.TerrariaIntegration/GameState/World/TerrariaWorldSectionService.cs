using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.GameState.World;

/// <summary>
/// Activation-scoped facade over the update-thread world-section cache. It never reads Terraria
/// state itself, so consumers can safely copy the detached frame from any thread.
/// </summary>
internal sealed class TerrariaWorldSectionService : IPluginWorldSectionService, IPluginRegistration
{
    private readonly TerrariaWorldSectionSnapshotCache cache;
    private readonly object gate = new object();
    private bool released;
    private bool demandRegistered;
    private int requestedMargin;

    private TerrariaWorldSectionService(TerrariaWorldSectionSnapshotCache cache)
    {
        this.cache = cache;
    }

    internal static IPluginWorldSectionService CreateService(
        TerrariaWorldSectionSnapshotCache cache,
        PluginManifest manifest,
        IPluginResourceScope resources)
    {
        if (cache == null) throw new ArgumentNullException(nameof(cache));
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.GameStateRead) == 0 ||
            (manifest.Permissions & PluginPermission.ReadGameState) == 0)
        {
            return new DeniedService(manifest.Id);
        }

        var service = new TerrariaWorldSectionService(cache);
        try
        {
            resources.Own("world-sections", PluginResourceKind.EventSubscription, service);
            return service;
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    public string Name => "world-sections";

    public bool IsReleased
    {
        get
        {
            lock (gate)
            {
                return released;
            }
        }
    }

    public void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));

        lock (gate)
        {
            if (released) throw new ObjectDisposedException("IPluginWorldSectionService");
            requestedMargin = margin;

            if (!demandRegistered)
            {
                cache.RegisterDemand(this);
                demandRegistered = true;
            }

        }

        cache.CopyVisibleSections(destination, margin);
    }

    internal int RequestedMargin
    {
        get { return System.Threading.Volatile.Read(ref requestedMargin); }
    }

    public void Dispose()
    {
        bool unregister;
        lock (gate)
        {
            if (released) return;
            released = true;
            unregister = demandRegistered;
            demandRegistered = false;
        }

        if (unregister)
        {
            cache.UnregisterDemand(this);
        }
    }

    private sealed class DeniedService : IPluginWorldSectionService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public void CopyVisibleSections(ICollection<PluginWorldSectionSnapshot> destination, int margin = 0)
        {
            throw new UnauthorizedAccessException(
                "Plugin '" + owner.Value + "' must declare GameStateRead capability and ReadGameState permission before reading world section snapshots.");
        }
    }
}
