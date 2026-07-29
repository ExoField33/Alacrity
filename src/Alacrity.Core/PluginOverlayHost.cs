using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned overlay registry and dispatch boundary. Terraria rendering integration supplies the canvas.</summary>
public sealed class PluginOverlayHost
{
    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<PluginId, DateTime> lastFailure = new Dictionary<PluginId, DateTime>();
    private readonly TimeSpan failureInterval;
    private Entry[] orderedSnapshot = Array.Empty<Entry>();

    public PluginOverlayHost(TimeSpan? failureInterval = null)
    {
        this.failureInterval = failureInterval ?? TimeSpan.FromSeconds(10);
    }

    public IPluginOverlayService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (!manifest.Id.IsValid) throw new ArgumentException("A valid plugin owner is required.", nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new Service(this, manifest, resources);
    }

    /// <summary>Dispatches in layer/order/registration order. The empty path allocates nothing.</summary>
    public void Dispatch(IPluginOverlayCanvas canvas, PluginOverlayFrame frame, IPluginLogger? diagnostics = null)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        Entry[] snapshot = Volatile.Read(ref orderedSnapshot);
        if (snapshot.Length == 0) return;
        foreach (Entry entry in snapshot)
        {
            try { entry.Draw(canvas, frame); }
            catch (Exception exception)
            {
                if (ShouldReport(entry.Owner)) diagnostics?.Error("Overlay '" + entry.Descriptor.Id + "' failed.", exception);
            }
        }
    }

    public int CountFor(PluginId owner)
    {
        lock (gate) return entries.Count(entry => entry.Owner == owner);
    }

    private IPluginRegistration Register(PluginManifest manifest, IPluginResourceScope resources, PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw)
    {
        if ((manifest.Capabilities & PluginCapability.Rendering) == 0 || (manifest.Permissions & PluginPermission.DrawUserInterface) == 0)
            throw new UnauthorizedAccessException("Overlay registrations require Rendering capability and DrawUserInterface permission.");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        Entry entry;
        lock (gate)
        {
            if (entries.Any(candidate => candidate.Owner == manifest.Id && string.Equals(candidate.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("The plugin already registered overlay '" + descriptor.Id + "'.");
            entry = new Entry(manifest.Id, descriptor, draw, entries.Count == 0 ? 0 : entries.Max(candidate => candidate.Sequence) + 1);
            entries.Add(entry);
            RebuildSnapshot();
        }
        var registration = new Registration("overlay:" + manifest.Id.Value + ":" + descriptor.Id, () => { lock (gate) { entries.Remove(entry); RebuildSnapshot(); } });
        resources.Own(registration.Name, PluginResourceKind.RenderingHandler, registration);
        return registration;
    }

    private bool ShouldReport(PluginId owner)
    {
        lock (gate)
        {
            DateTime now = DateTime.UtcNow;
            if (lastFailure.TryGetValue(owner, out DateTime previous) && now - previous < failureInterval) return false;
            lastFailure[owner] = now;
            return true;
        }
    }
    private void RebuildSnapshot() => Volatile.Write(ref orderedSnapshot, entries.OrderBy(entry => entry.Descriptor.Layer).ThenBy(entry => entry.Descriptor.Order).ThenBy(entry => entry.Sequence).ToArray());

    private sealed class Service : IPluginOverlayService
    {
        private readonly PluginOverlayHost host; private readonly PluginManifest manifest; private readonly IPluginResourceScope resources;
        public Service(PluginOverlayHost host, PluginManifest manifest, IPluginResourceScope resources) { this.host = host; this.manifest = manifest; this.resources = resources; }
        public IPluginRegistration Register(PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw) => host.Register(manifest, resources, descriptor, draw);
    }

    private sealed class Entry
    {
        public Entry(PluginId owner, PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw, long sequence) { Owner = owner; Descriptor = descriptor; Draw = draw; Sequence = sequence; }
        public PluginId Owner { get; }
        public PluginOverlayDescriptor Descriptor { get; }
        public Action<IPluginOverlayCanvas, PluginOverlayFrame> Draw { get; }
        public long Sequence { get; }
    }

    private sealed class Registration : IPluginRegistration
    {
        private readonly Action release; private bool released;
        public Registration(string name, Action release) { Name = name; this.release = release; }
        public string Name { get; }
        public bool IsReleased => released;
        public void Dispose() { if (released) return; released = true; release(); }
    }
}
