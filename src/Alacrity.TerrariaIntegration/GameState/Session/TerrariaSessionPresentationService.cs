using System;
using System.Reflection;
using System.Threading;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria.GameState.Session;

/// <summary>
/// Publishes detached session presentation data from Terraria's update boundary. Capture is lazy:
/// merely creating a permitted service does not read networking state until that activation asks
/// for a snapshot. Two reusable frames use a sequence check so worker-thread readers receive one
/// coherent value without allocating or locking Terraria's update thread.
/// </summary>
internal sealed class TerrariaSessionPresentationService
{
    private const int PingSampleIntervalMilliseconds = 250;
    private readonly SessionFrame[] frames =
    {
        new SessionFrame(),
        new SessionFrame()
    };
    private int publishedFrame;
    private int demandCount;
    private int demandGeneration;
    private uint capturedTick = uint.MaxValue;
    private Func<int> pingGetter;
    private int pingLookupAttempted;
    private int cachedPing = -1;
    private int nextPingSampleTick;

    /// <summary>Captures at most once per update, and only while an activation reads session data.</summary>
    internal void CaptureForCurrentTick()
    {
        if (Volatile.Read(ref demandCount) == 0)
        {
            return;
        }

        uint tick = Main.GameUpdateCount;
        if (tick == Volatile.Read(ref capturedTick))
        {
            return;
        }

        int generation = Volatile.Read(ref demandGeneration);
        if (Volatile.Read(ref demandCount) == 0 || generation != Volatile.Read(ref demandGeneration))
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(Main.worldName) ? "Server" : Main.worldName;
        int capacity = Math.Max(0, Main.maxPlayers);
        int ping = GetPingAtUpdateBoundary();
        if (Volatile.Read(ref demandCount) == 0 || generation != Volatile.Read(ref demandGeneration))
        {
            return;
        }

        int writeFrame = 1 - Volatile.Read(ref publishedFrame);
        frames[writeFrame].Write(name, capacity, ping);
        Volatile.Write(ref publishedFrame, writeFrame);
        Volatile.Write(ref capturedTick, tick);
    }

    internal IPluginSessionPresentationService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if ((manifest.Capabilities & PluginCapability.MultiplayerObservation) == 0 ||
            (manifest.Permissions & PluginPermission.ObserveMultiplayer) == 0)
        {
            return new DeniedService(manifest.Id);
        }

        var service = new ScopedService(this);
        try
        {
            resources.Own("session-presentation", PluginResourceKind.EventSubscription, service);
            return service;
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    internal int DemandCount => Volatile.Read(ref demandCount);

    internal void PublishForTests(string name, int capacity, int? ping, uint tick)
    {
        int writeFrame = 1 - Volatile.Read(ref publishedFrame);
        frames[writeFrame].Write(name, capacity, ping.GetValueOrDefault(-1));
        Volatile.Write(ref publishedFrame, writeFrame);
        Volatile.Write(ref capturedTick, tick);
    }

    private PluginSessionPresentationSnapshot GetCurrent()
    {
        for (;;)
        {
            SessionFrame frame = frames[Volatile.Read(ref publishedFrame)];
            int before = Volatile.Read(ref frame.Sequence);
            if ((before & 1) != 0)
            {
                continue;
            }

            string name = Volatile.Read(ref frame.ServerName);
            int capacity = Volatile.Read(ref frame.PlayerCapacity);
            int ping = Volatile.Read(ref frame.PingMilliseconds);
            if (before == Volatile.Read(ref frame.Sequence))
            {
                return new PluginSessionPresentationSnapshot(name, capacity, ping < 0 ? (int?)null : ping);
            }
        }
    }

    private void RegisterDemand()
    {
        if (Interlocked.Increment(ref demandCount) == 1)
        {
            Interlocked.Increment(ref demandGeneration);
            Volatile.Write(ref capturedTick, uint.MaxValue);
        }
    }

    private void UnregisterDemand()
    {
        if (Interlocked.Decrement(ref demandCount) == 0)
        {
            Interlocked.Increment(ref demandGeneration);
            frames[0].Write("Server", 0, -1);
            frames[1].Write("Server", 0, -1);
            Volatile.Write(ref capturedTick, uint.MaxValue);
        }
    }

    private int GetPingAtUpdateBoundary()
    {
        int now = Environment.TickCount;
        if (unchecked(now - Volatile.Read(ref nextPingSampleTick)) < 0)
        {
            return Volatile.Read(ref cachedPing);
        }

        Volatile.Write(ref nextPingSampleTick, unchecked(now + PingSampleIntervalMilliseconds));
        if (Interlocked.CompareExchange(ref pingLookupAttempted, 1, 0) == 0)
        {
            try
            {
                Type type = typeof(Main).Assembly.GetType("Terraria.Net.Ping", false);
                PropertyInfo candidate = type == null
                    ? null
                    : type.GetProperty("CurrentPing", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (candidate?.PropertyType == typeof(int) && candidate.GetMethod != null && candidate.GetMethod.IsStatic)
                {
                    pingGetter = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), candidate.GetMethod);
                }
            }
            catch
            {
                pingGetter = null;
            }
        }

        try
        {
            int ping = pingGetter == null ? -1 : pingGetter();
            Volatile.Write(ref cachedPing, ping >= 0 ? ping : -1);
        }
        catch
        {
            Volatile.Write(ref cachedPing, -1);
        }

        return Volatile.Read(ref cachedPing);
    }

    private sealed class ScopedService : IPluginSessionPresentationService, IDisposable
    {
        private readonly TerrariaSessionPresentationService owner;
        private readonly object gate = new object();
        private bool demandRegistered;
        private bool released;

        internal ScopedService(TerrariaSessionPresentationService owner)
        {
            this.owner = owner;
        }

        public PluginSessionPresentationSnapshot GetCurrent()
        {
            lock (gate)
            {
                if (released)
                {
                    throw new ObjectDisposedException("IPluginSessionPresentationService", "The owning plugin scope has been released.");
                }

                if (!demandRegistered)
                {
                    owner.RegisterDemand();
                    demandRegistered = true;
                }
            }

            return owner.GetCurrent();
        }

        public void Dispose()
        {
            bool unregister;
            lock (gate)
            {
                if (released)
                {
                    return;
                }

                released = true;
                unregister = demandRegistered;
                demandRegistered = false;
            }

            if (unregister)
            {
                owner.UnregisterDemand();
            }
        }
    }

    private sealed class DeniedService : IPluginSessionPresentationService
    {
        private readonly PluginId owner;

        internal DeniedService(PluginId owner)
        {
            this.owner = owner;
        }

        public PluginSessionPresentationSnapshot GetCurrent()
        {
            throw new UnauthorizedAccessException("Plugin '" + owner.Value + "' must declare MultiplayerObservation capability and ObserveMultiplayer permission before reading session presentation data.");
        }
    }

    private sealed class SessionFrame
    {
        internal string ServerName = "Server";
        internal int PlayerCapacity;
        internal int PingMilliseconds = -1;
        internal int Sequence;

        internal void Write(string serverName, int playerCapacity, int pingMilliseconds)
        {
            Interlocked.Increment(ref Sequence);
            Volatile.Write(ref ServerName, serverName ?? string.Empty);
            Volatile.Write(ref PlayerCapacity, Math.Max(0, playerCapacity));
            Volatile.Write(ref PingMilliseconds, pingMilliseconds < 0 ? -1 : pingMilliseconds);
            Interlocked.Increment(ref Sequence);
        }
    }
}
