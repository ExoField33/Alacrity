using System;
using System.Collections.Generic;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned composition of scoped presentation policies. Disabled effects win; exceptions union.</summary>
public sealed class PluginVisualEffectsHost
{
    private readonly object gate = new object();
    private readonly List<Entry> entries = new List<Entry>();
    private PluginVisualEffectsPolicy effective = new PluginVisualEffectsPolicy(true, true);

    public IPluginVisualEffectsService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        return new Service(this, manifest, resources);
    }

    public PluginVisualEffectsPolicy GetEffectivePolicy() { lock (gate) return effective; }

    private IPluginRegistration Register(PluginManifest manifest, IPluginResourceScope resources, PluginVisualEffectsPolicy policy)
    {
        if ((manifest.Capabilities & (PluginCapability.Rendering | PluginCapability.GameStateRead)) != (PluginCapability.Rendering | PluginCapability.GameStateRead) ||
            (manifest.Permissions & (PluginPermission.DrawUserInterface | PluginPermission.ReadGameState)) != (PluginPermission.DrawUserInterface | PluginPermission.ReadGameState))
            throw new UnauthorizedAccessException("Visual-effects policies require Rendering and GameStateRead capabilities plus DrawUserInterface and ReadGameState permissions.");
        if (policy == null) throw new ArgumentNullException(nameof(policy));
        var entry = new Entry(policy);
        lock (gate) { entries.Add(entry); Rebuild(); }
        var registration = new Registration(() => { lock (gate) { entries.Remove(entry); Rebuild(); } });
        resources.Own("visual-effects-policy", PluginResourceKind.RenderingHandler, registration);
        return registration;
    }

    private void Rebuild()
    {
        bool dust = true, gore = true;
        var exceptions = new HashSet<int>();
        for (int index = 0; index < entries.Count; index++)
        {
            PluginVisualEffectsPolicy policy = entries[index].Policy;
            dust &= policy.DustEnabled;
            gore &= policy.GoreEnabled;
            foreach (int id in policy.DustExceptionIds) if (id >= 0) exceptions.Add(id);
        }
        var orderedExceptions = new List<int>(exceptions);
        orderedExceptions.Sort();
        effective = new PluginVisualEffectsPolicy(dust, gore, orderedExceptions);
    }

    private sealed class Service : IPluginVisualEffectsService
    {
        private readonly PluginVisualEffectsHost host; private readonly PluginManifest manifest; private readonly IPluginResourceScope resources;
        public Service(PluginVisualEffectsHost host, PluginManifest manifest, IPluginResourceScope resources) { this.host = host; this.manifest = manifest; this.resources = resources; }
        public IPluginRegistration RegisterPolicy(PluginVisualEffectsPolicy policy) => host.Register(manifest, resources, policy);
    }
    private sealed class Entry { public Entry(PluginVisualEffectsPolicy policy) { Policy = policy; } public PluginVisualEffectsPolicy Policy { get; } }
    private sealed class Registration : IPluginRegistration { private readonly Action release; private bool released; public Registration(Action release) { this.release = release; } public string Name => "visual-effects-policy"; public bool IsReleased => released; public void Dispose() { if (released) return; released = true; release(); } }
}
