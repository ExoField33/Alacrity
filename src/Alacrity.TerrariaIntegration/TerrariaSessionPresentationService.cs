using System;
using System.Reflection;
using System.Threading;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria;

/// <summary>Captures detached session data at the update boundary for scope-guarded plugin readers.</summary>
internal sealed class TerrariaSessionPresentationService
{
    private SessionFrame current = new SessionFrame(new PluginSessionPresentationSnapshot("Server", 0, null));
    private uint capturedTick = uint.MaxValue;
    private PropertyInfo pingProperty;
    private bool pingLookupAttempted;
    private int? cachedPing;
    private DateTime nextPingSampleUtc;

    internal void CaptureForCurrentTick()
    {
        uint tick = Main.GameUpdateCount;
        if (tick == Volatile.Read(ref capturedTick)) return;
        string name = string.IsNullOrWhiteSpace(Main.worldName) ? "Server" : Main.worldName;
        Volatile.Write(ref current, new SessionFrame(new PluginSessionPresentationSnapshot(name, Math.Max(0, Main.maxPlayers), GetPingAtUpdateBoundary())));
        Volatile.Write(ref capturedTick, tick);
    }

    internal IPluginSessionPresentationService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.MultiplayerObservation) == 0 || (manifest.Permissions & PluginPermission.ObserveMultiplayer) == 0)
            return new DeniedService(manifest.Id);
        var guard = new ScopeGuard();
        try { resources.Own("session-presentation", PluginResourceKind.EventSubscription, guard); }
        catch { guard.Dispose(); throw; }
        return new ScopedService(this, guard);
    }

    private int? GetPingAtUpdateBoundary()
    {
        DateTime now = DateTime.UtcNow;
        if (now < nextPingSampleUtc) return cachedPing;
        nextPingSampleUtc = now.AddMilliseconds(250);
        try
        {
            if (!pingLookupAttempted)
            {
                pingLookupAttempted = true;
                Type type = Type.GetType("Terraria.Net.Ping, Terraria", false);
                PropertyInfo candidate = type?.GetProperty("CurrentPing", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (candidate != null && candidate.PropertyType == typeof(int) && candidate.GetMethod != null && candidate.GetMethod.IsStatic) pingProperty = candidate;
            }
            object value = pingProperty?.GetValue(null, null);
            cachedPing = value is int ping && ping >= 0 ? ping : (int?)null;
        }
        catch { cachedPing = null; }
        return cachedPing;
    }

    private sealed class ScopedService : IPluginSessionPresentationService
    {
        private readonly TerrariaSessionPresentationService owner; private readonly ScopeGuard guard;
        internal ScopedService(TerrariaSessionPresentationService owner, ScopeGuard guard) { this.owner = owner; this.guard = guard; }
        public PluginSessionPresentationSnapshot GetCurrent()
        {
            if (guard.IsReleased) throw new ObjectDisposedException("IPluginSessionPresentationService", "The owning plugin scope has been released.");
            return Volatile.Read(ref owner.current).Value;
        }
    }

    private sealed class DeniedService : IPluginSessionPresentationService
    {
        private readonly PluginId owner; internal DeniedService(PluginId owner) { this.owner = owner; }
        public PluginSessionPresentationSnapshot GetCurrent() => throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare MultiplayerObservation capability and ObserveMultiplayer permission before reading session presentation data.");
    }

    private sealed class ScopeGuard : IDisposable
    {
        private int released; internal bool IsReleased => Volatile.Read(ref released) != 0;
        public void Dispose() { Interlocked.Exchange(ref released, 1); }
    }

    private sealed class SessionFrame
    {
        internal SessionFrame(PluginSessionPresentationSnapshot value) { Value = value; }
        internal PluginSessionPresentationSnapshot Value { get; }
    }
}
