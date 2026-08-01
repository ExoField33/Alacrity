using System;
using System.Collections.Generic;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Owns retained HUD widgets and dispatches them through an integration-provided safe canvas.</summary>
public sealed class PluginHudHost
{
    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, DateTime> lastFailure = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private readonly TimeSpan failureInterval;
    private Entry[] snapshot = Array.Empty<Entry>();
    private bool snapshotDirty = true;
    private long sequence;

    public PluginHudHost(TimeSpan? failureInterval = null)
    {
        this.failureInterval = failureInterval ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>Creates the plugin-scoped registration service.</summary>
    public IPluginHudService CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger = null)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.UserInterface) == 0 || (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
            return new DeniedService(manifest.Id);
        var guard = new ScopeGuard();
        try { resources.Own("hud", PluginResourceKind.UserInterface, guard); }
        catch { guard.Dispose(); throw; }
        return new ScopedService(this, manifest.Id, resources, logger, guard);
    }

    /// <summary>Draws an immutable ordered widget snapshot. Plugin failures are isolated per widget.</summary>
    public void Dispatch(IPluginHudRenderer renderer, PluginHudFrame frame)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        Entry[] active;
        lock (gate)
        {
            if (snapshotDirty)
            {
                snapshot = entries.OrderBy(entry => entry.Descriptor.Order).ThenBy(entry => entry.Sequence).ToArray();
                snapshotDirty = false;
            }
            active = snapshot;
        }
        for (int index = 0; index < active.Length; index++)
        {
            Entry entry = active[index];
            if (entry.IsSuspended) continue;
            try
            {
                renderer.Render(entry.Owner, entry.Descriptor, entry.Draw, frame);
                entry.RecordSuccess(DateTime.UtcNow, failureInterval);
            }
            catch (Exception exception)
            {
                entry.RecordFailure(DateTime.UtcNow, failureInterval);
                if (ShouldReport(entry.Owner, entry.Descriptor.Id))
                    entry.Logger?.Error("HUD widget '" + entry.Descriptor.Id + "' failed for plugin '" + entry.Owner.Value + "'.", exception);
            }
        }
    }

    private IPluginRegistration Register(PluginId owner, IPluginResourceScope resources, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, IPluginLogger? logger)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        Entry entry;
        lock (gate)
        {
            if (entries.Any(entry => entry.Owner == owner && string.Equals(entry.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered HUD widget '" + descriptor.Id + "'.");
            entry = new Entry(owner, descriptor, draw, ++sequence, Remove, logger);
            entries.Add(entry); snapshotDirty = true;
        }
        try
        {
            resources.Own("hud-widget:" + descriptor.Id, PluginResourceKind.RenderingHandler, entry);
        }
        catch
        {
            entry.Dispose();
            throw;
        }
        return entry;
    }

    private bool ShouldReport(PluginId owner, string widgetId)
    {
        lock (gate)
        {
            string key = owner.Value + ":" + widgetId;
            DateTime now = DateTime.UtcNow;
            if (lastFailure.TryGetValue(key, out DateTime previous) && now - previous < failureInterval) return false;
            lastFailure[key] = now;
            return true;
        }
    }

    private void Remove(Entry entry)
    {
        lock (gate)
            if (entries.Remove(entry)) snapshotDirty = true;
    }

    private sealed class ScopedService : IPluginHudService
    {
        private readonly PluginHudHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources; private readonly IPluginLogger? logger; private readonly ScopeGuard guard;
        internal ScopedService(PluginHudHost host, PluginId owner, IPluginResourceScope resources, IPluginLogger? logger, ScopeGuard guard) { this.host = host; this.owner = owner; this.resources = resources; this.logger = logger; this.guard = guard; }
        public IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw)
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginHudService", "The owning plugin scope has been released.");
            return host.Register(owner, resources, descriptor, draw, logger);
        }
    }

    private sealed class DeniedService : IPluginHudService
    {
        private readonly PluginId owner; internal DeniedService(PluginId owner) { this.owner = owner; }
        public IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw) => throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare UserInterface capability and DrawUserInterface permission before registering HUD widgets.");
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;
        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); }
    }

    private sealed class Entry : IPluginRegistration
    {
        private readonly Action<Entry> remove; private readonly IPluginLogger? logger; private readonly object failureGate = new object(); private bool released; private int failureCount; private DateTime failureWindowStartedUtc;
        internal Entry(PluginId owner, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, long sequence, Action<Entry> remove, IPluginLogger? logger) { Owner = owner; Descriptor = descriptor; Draw = draw; Sequence = sequence; this.remove = remove; this.logger = logger; }
        internal PluginId Owner { get; } internal PluginHudWidgetDescriptor Descriptor { get; } internal Action<IPluginHudCanvas, PluginHudFrame> Draw { get; } internal long Sequence { get; }
        internal IPluginLogger? Logger => logger;
        internal bool IsSuspended { get { lock (failureGate) return failureCount >= 3; } }
        public string Name => "hud-widget:" + Descriptor.Id; public bool IsReleased => released;
        public void Dispose() { if (released) return; released = true; remove(this); }
        internal void RecordFailure(DateTime now, TimeSpan window)
        {
            lock (failureGate)
            {
                if (failureWindowStartedUtc == default(DateTime) || now - failureWindowStartedUtc > window) { failureWindowStartedUtc = now; failureCount = 1; }
                else failureCount++;
            }
        }
        internal void RecordSuccess(DateTime now, TimeSpan window)
        {
            lock (failureGate)
                if (failureWindowStartedUtc != default(DateTime) && now - failureWindowStartedUtc > window) { failureWindowStartedUtc = default(DateTime); failureCount = 0; }
        }
    }
}
