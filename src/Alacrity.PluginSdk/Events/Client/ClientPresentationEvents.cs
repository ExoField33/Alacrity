using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Published from the verified Terraria update hook when the client transitions between the menu
/// and active gameplay. It is observational only and is delivered on Terraria's main update thread.
/// </summary>
public readonly struct ClientMenuStateChangedEvent : IMainThreadPluginEvent, INonCancellablePluginEvent
{
    /// <summary>Creates the menu-state transition snapshot.</summary>
    public ClientMenuStateChangedEvent(bool isGameMenu, uint simulationTick, TimeSpan timestamp)
    {
        IsGameMenu = isGameMenu;
        SimulationTick = simulationTick;
        Timestamp = timestamp;
    }

    /// <summary>Whether the client is currently presenting the main/menu UI rather than gameplay.</summary>
    public bool IsGameMenu { get; }
    /// <summary>Terraria simulation tick that observed the transition.</summary>
    public uint SimulationTick { get; }
    /// <summary>Monotonic host timestamp for the observed transition.</summary>
    public TimeSpan Timestamp { get; }
}
