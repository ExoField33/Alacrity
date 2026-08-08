using System;

namespace Alacrity.PluginSdk;

/// <summary>Published once after the host has created and restored the plugin runtime.</summary>
public readonly struct ClientStartedEvent : IMainThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates the startup event from a monotonic host timestamp.</summary>
    public ClientStartedEvent(TimeSpan timestamp) { Timestamp = timestamp; }
    /// <summary>Monotonic elapsed host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}

/// <summary>Published before activation scopes begin their deterministic shutdown cleanup.</summary>
public readonly struct ClientShuttingDownEvent : IMainThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates the shutdown event from a monotonic host timestamp.</summary>
    public ClientShuttingDownEvent(TimeSpan timestamp) { Timestamp = timestamp; }
    /// <summary>Monotonic elapsed host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}

/// <summary>
/// Published from the verified Terraria gameplay-update hook after shared host snapshots have
/// been refreshed. It is delivered on Terraria's main update thread and describes completed work.
/// </summary>
public readonly struct ClientUpdatedEvent : IMainThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates the update event from the host's simulation tick and monotonic timestamp.</summary>
    public ClientUpdatedEvent(uint simulationTick, TimeSpan timestamp)
    {
        SimulationTick = simulationTick;
        Timestamp = timestamp;
    }

    /// <summary>The current host simulation tick.</summary>
    public uint SimulationTick { get; }

    /// <summary>Monotonic host presentation time.</summary>
    public TimeSpan Timestamp { get; }
}
