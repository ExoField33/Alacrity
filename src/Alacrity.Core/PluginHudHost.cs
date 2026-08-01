using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Owns retained HUD widgets and dispatches them through an integration-provided safe canvas.</summary>
public sealed class PluginHudHost
{
    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private Entry[] snapshot = Array.Empty<Entry>();
    private bool snapshotDirty = true;
    private long sequence;

    /// <summary>Creates the plugin-scoped registration service.</summary>
    public IPluginHudService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.UserInterface) == 0 || (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
            return new DeniedService(manifest.Id);
        return new ScopedService(this, manifest.Id, resources);
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
            try { renderer.Render(entry.Owner, entry.Descriptor, entry.Draw, frame); }
            catch (Exception exception) { Trace.TraceError("Alacrity HUD widget '" + entry.Descriptor.Id + "' failed for plugin '" + entry.Owner.Value + "': " + exception); }
        }
    }

    private IPluginRegistration Register(PluginId owner, IPluginResourceScope resources, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        lock (gate)
        {
            if (entries.Any(entry => entry.Owner == owner && string.Equals(entry.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered HUD widget '" + descriptor.Id + "'.");
            var entry = new Entry(owner, descriptor, draw, ++sequence, Remove);
            entries.Add(entry); snapshotDirty = true;
            resources.Own("hud-widget:" + descriptor.Id, PluginResourceKind.RenderingHandler, entry);
            return entry;
        }
    }

    private void Remove(Entry entry)
    {
        lock (gate)
            if (entries.Remove(entry)) snapshotDirty = true;
    }

    private sealed class ScopedService : IPluginHudService
    {
        private readonly PluginHudHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources;
        internal ScopedService(PluginHudHost host, PluginId owner, IPluginResourceScope resources) { this.host = host; this.owner = owner; this.resources = resources; }
        public IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw)
        {
            if (resources.IsDisposed) throw new ObjectDisposedException("IPluginHudService");
            return host.Register(owner, resources, descriptor, draw);
        }
    }

    private sealed class DeniedService : IPluginHudService
    {
        private readonly PluginId owner; internal DeniedService(PluginId owner) { this.owner = owner; }
        public IPluginRegistration Register(PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw) => throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare UserInterface capability and DrawUserInterface permission before registering HUD widgets.");
    }

    private sealed class Entry : IPluginRegistration
    {
        private readonly Action<Entry> remove; private bool released;
        internal Entry(PluginId owner, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, long sequence, Action<Entry> remove) { Owner = owner; Descriptor = descriptor; Draw = draw; Sequence = sequence; this.remove = remove; }
        internal PluginId Owner { get; } internal PluginHudWidgetDescriptor Descriptor { get; } internal Action<IPluginHudCanvas, PluginHudFrame> Draw { get; } internal long Sequence { get; }
        public string Name => "hud-widget:" + Descriptor.Id; public bool IsReleased => released;
        public void Dispose() { if (released) return; released = true; remove(this); }
    }
}
