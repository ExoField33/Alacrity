using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Published from the verified Terraria update hook when local player-chat input gains or loses
/// focus. The event is observational, non-cancellable, and delivered on the main update thread.
/// </summary>
public readonly struct ChatInputStateChangedEvent : IMainThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates the chat-input focus transition snapshot.</summary>
    public ChatInputStateChangedEvent(bool isOpen, uint simulationTick, TimeSpan timestamp)
    {
        IsOpen = isOpen;
        SimulationTick = simulationTick;
        Timestamp = timestamp;
    }

    /// <summary>Whether Terraria's local player-chat input is currently active.</summary>
    public bool IsOpen { get; }
    /// <summary>Terraria simulation tick that observed the transition.</summary>
    public uint SimulationTick { get; }
    /// <summary>Monotonic host timestamp for the observed transition.</summary>
    public TimeSpan Timestamp { get; }
}
