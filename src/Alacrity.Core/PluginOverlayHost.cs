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
    private readonly Dictionary<string, DateTime> lastFailure = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private readonly TimeSpan failureInterval;
    private readonly Func<DateTime> utcNow;
    private long nextSequence;
    // Each draw phase reads one immutable array. Rebuilding happens only on registration changes.
    private Entry[][] phaseSnapshots = CreateEmptyPhaseSnapshots();

    public PluginOverlayHost(TimeSpan? failureInterval = null, Func<DateTime>? utcNow = null)
    {
        this.failureInterval = failureInterval ?? TimeSpan.FromSeconds(10);
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IPluginOverlayService CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger = null)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (!manifest.Id.IsValid) throw new ArgumentException("A valid plugin owner is required.", nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        var guard = new ScopeGuard();
        try { resources.Own("overlays", PluginResourceKind.RenderingHandler, guard); }
        catch { guard.Dispose(); throw; }
        return new Service(this, manifest, resources, logger, guard);
    }

    /// <summary>Dispatches in layer/order/registration order. The empty path allocates nothing.</summary>
    public void Dispatch(IPluginOverlayCanvas canvas, PluginOverlayFrame frame, IPluginLogger? diagnostics = null)
    {
        Dispatch(canvas, frame, PluginOverlaySpace.World, diagnostics);
    }

    /// <summary>Dispatches only contributions registered for the host's current verified draw phase.</summary>
    public void Dispatch(IPluginOverlayCanvas canvas, PluginOverlayFrame frame, PluginOverlaySpace space, IPluginLogger? diagnostics = null)
    {
        if (canvas == null) throw new ArgumentNullException(nameof(canvas));
        if (!IsValidOverlaySpace(space)) throw new ArgumentOutOfRangeException(nameof(space));
        Entry[][] snapshots = Volatile.Read(ref phaseSnapshots);
        Entry[] snapshot = snapshots[(int)space];
        if (snapshot.Length == 0) return;
        for (int index = 0; index < snapshot.Length; index++)
        {
            Entry entry = snapshot[index];
            if (!entry.CanInvoke(utcNow, out DateTime now)) continue;
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease)) continue;
            try
            {
                using (lease)
                {
                    entry.Draw(canvas, frame);
                }
                entry.RecordSuccess();
            }
            catch (Exception exception)
            {
                if (now == default) now = utcNow();
                bool report = ShouldReport(entry.Owner, entry.Descriptor.Id, now);
                entry.RecordFailure(now, failureInterval);
                if (report) (entry.Logger ?? diagnostics)?.Error("Overlay '" + entry.Descriptor.Id + "' in layer " + entry.Descriptor.Layer + " failed for plugin '" + entry.Owner.Value + "'.", exception);
            }
        }
    }

    public int CountFor(PluginId owner)
    {
        lock (gate) return entries.Count(entry => entry.Owner == owner);
    }

    /// <summary>Returns whether the specified draw phase has active registrations without allocating.</summary>
    public bool HasRegistrations(PluginOverlaySpace space)
    {
        if (!IsValidOverlaySpace(space)) throw new ArgumentOutOfRangeException(nameof(space));
        return Volatile.Read(ref phaseSnapshots)[(int)space].Length != 0;
    }

    private IPluginRegistration Register(PluginManifest manifest, IPluginResourceScope resources, PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw, IPluginLogger? logger)
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
            entry = new Entry(manifest.Id, descriptor, draw, logger, nextSequence++, ActivationCallbackGates.TryGet(resources));
        }
        var registration = new Registration("overlay:" + manifest.Id.Value + ":" + descriptor.Id, () => { lock (gate) { entries.Remove(entry); RebuildSnapshot(); } });
        try
        {
            resources.Own(registration.Name, PluginResourceKind.RenderingHandler, registration);
        }
        catch
        {
            registration.Dispose();
            throw;
        }
        bool releaseAfterCommit = false;
        bool scopeReleasedDuringCommit = false;
        lock (gate)
        {
            scopeReleasedDuringCommit = registration.IsReleased;
            if (scopeReleasedDuringCommit || entries.Any(candidate => candidate.Owner == manifest.Id && string.Equals(candidate.Descriptor.Id, descriptor.Id, StringComparison.Ordinal)))
            {
                releaseAfterCommit = true;
            }
            else
            {
                entries.Add(entry);
                RebuildSnapshot();
            }
        }
        if (releaseAfterCommit)
        {
            registration.Dispose();
            if (scopeReleasedDuringCommit) throw new ObjectDisposedException("IPluginResourceScope", "The owning plugin scope was released during overlay registration.");
            throw new InvalidOperationException("The plugin already registered overlay '" + descriptor.Id + "'.");
        }
        return registration;
    }

    private bool ShouldReport(PluginId owner, string overlayId, DateTime now)
    {
        lock (gate)
        {
            string key = owner.Value + ":" + overlayId;
            if (lastFailure.TryGetValue(key, out DateTime previous) && now - previous < failureInterval) return false;
            lastFailure[key] = now;
            return true;
        }
    }
    private void RebuildSnapshot()
    {
        Entry[] ordered = entries.OrderBy(entry => entry.Descriptor.Layer).ThenBy(entry => entry.Descriptor.Order).ThenBy(entry => entry.Sequence).ToArray();
        var world = new List<Entry>();
        var hud = new List<Entry>();
        var menu = new List<Entry>();
        for (int index = 0; index < ordered.Length; index++)
        {
            Entry entry = ordered[index];
            switch (entry.Descriptor.Space)
            {
                case PluginOverlaySpace.World: world.Add(entry); break;
                case PluginOverlaySpace.Hud: hud.Add(entry); break;
                case PluginOverlaySpace.Menu: menu.Add(entry); break;
            }
        }
        Volatile.Write(ref phaseSnapshots, new[]
        {
            world.Count == 0 ? Array.Empty<Entry>() : world.ToArray(),
            hud.Count == 0 ? Array.Empty<Entry>() : hud.ToArray(),
            menu.Count == 0 ? Array.Empty<Entry>() : menu.ToArray()
        });
    }

    private static Entry[][] CreateEmptyPhaseSnapshots()
    {
        return new[] { Array.Empty<Entry>(), Array.Empty<Entry>(), Array.Empty<Entry>() };
    }

    private static bool IsValidOverlaySpace(PluginOverlaySpace space)
    {
        return (uint)space <= (uint)PluginOverlaySpace.Menu;
    }

    private sealed class Service : IPluginOverlayService
    {
        private readonly PluginOverlayHost host; private readonly PluginManifest manifest; private readonly IPluginResourceScope resources; private readonly IPluginLogger? logger; private readonly ScopeGuard guard;
        public Service(PluginOverlayHost host, PluginManifest manifest, IPluginResourceScope resources, IPluginLogger? logger, ScopeGuard guard) { this.host = host; this.manifest = manifest; this.resources = resources; this.logger = logger; this.guard = guard; }
        public IPluginRegistration Register(PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw)
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginOverlayService", "The owning plugin scope has been released.");
            return host.Register(manifest, resources, descriptor, draw, logger);
        }
    }

    private sealed class Entry
    {
        private readonly PluginFailureWindow failures = new PluginFailureWindow();
        private readonly ActivationCallbackGate? callbackGate;
        public Entry(PluginId owner, PluginOverlayDescriptor descriptor, Action<IPluginOverlayCanvas, PluginOverlayFrame> draw, IPluginLogger? logger, long sequence, ActivationCallbackGate? callbackGate) { Owner = owner; Descriptor = descriptor; Draw = draw; Logger = logger; Sequence = sequence; this.callbackGate = callbackGate; }
        public PluginId Owner { get; }
        public PluginOverlayDescriptor Descriptor { get; }
        public Action<IPluginOverlayCanvas, PluginOverlayFrame> Draw { get; }
        public IPluginLogger? Logger { get; }
        public long Sequence { get; }
        public bool CanInvoke(Func<DateTime> utcNow, out DateTime now) => failures.CanInvoke(utcNow, out now);
        public bool TryEnter(out ActivationCallbackGate.Lease lease)
        {
            if (callbackGate == null) { lease = default; return true; }
            return callbackGate.TryEnter(out lease);
        }
        public void RecordFailure(DateTime now, TimeSpan window)
        {
            failures.RecordFailure(now, window);
        }
        public void RecordSuccess()
        {
            failures.RecordSuccess();
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released;
        internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); }
    }

    private sealed class Registration : IPluginRegistration
    {
        private readonly Action release;
        private int released;
        public Registration(string name, Action release) { Name = name; this.release = release; }
        public string Name { get; }
        public bool IsReleased => System.Threading.Volatile.Read(ref released) != 0;
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref released, 1) == 0)
            {
                release();
            }
        }
    }
}
