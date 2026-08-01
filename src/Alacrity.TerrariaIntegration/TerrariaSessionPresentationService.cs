using System;
using System.Reflection;
using Alacrity.PluginSdk;
using Terraria;

namespace AlacrityTerraria;

/// <summary>Samples world/session presentation values without exposing multiplayer internals to plugins.</summary>
internal sealed class TerrariaSessionPresentationService : IPluginSessionPresentationService
{
    private PropertyInfo pingProperty;
    private bool pingLookupAttempted;
    private int? cachedPing;
    private DateTime nextSampleUtc;

    public PluginSessionPresentationSnapshot GetCurrent()
    {
        string name = string.IsNullOrWhiteSpace(Main.worldName) ? "Server" : Main.worldName;
        return new PluginSessionPresentationSnapshot(name, Math.Max(0, Main.maxPlayers), GetPing());
    }

    private int? GetPing()
    {
        DateTime now = DateTime.UtcNow;
        if (now < nextSampleUtc) return cachedPing;
        nextSampleUtc = now.AddMilliseconds(250);
        try
        {
            if (!pingLookupAttempted)
            {
                pingLookupAttempted = true;
                Type type = Type.GetType("Terraria.Net.Ping, Terraria", false);
                pingProperty = type?.GetProperty("CurrentPing", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            object value = pingProperty?.GetValue(null, null);
            cachedPing = value is int ping && ping >= 0 ? ping : (int?)null;
        }
        catch { cachedPing = null; }
        return cachedPing;
    }
}
