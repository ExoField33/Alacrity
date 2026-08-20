using System;
using Microsoft.Xna.Framework.Input;

namespace AlacrityTerraria.UserInterface;

/// <summary>
/// Per-search-field repeat state. Search inputs use raw keyboard state rather than Terraria's
/// append-only text helper, so held editing keys need the same bounded repeat behavior everywhere.
/// </summary>
internal struct PluginSearchKeyRepeatState
{
    private const int InitialDelayMilliseconds = 320;
    private const int RepeatIntervalMilliseconds = 38;

    private bool held;
    private int startTick;
    private int lastTick;

    internal bool ShouldRepeat(KeyboardState current, KeyboardState previous, Keys key)
    {
        if (!current.IsKeyDown(key))
        {
            this = default;
            return false;
        }

        int now = Environment.TickCount;
        if (!previous.IsKeyDown(key) || !held)
        {
            held = true;
            startTick = now;
            lastTick = now;
            return true;
        }

        if (Elapsed(now, startTick) < InitialDelayMilliseconds ||
            Elapsed(now, lastTick) < RepeatIntervalMilliseconds)
        {
            return false;
        }

        lastTick = now;
        return true;
    }

    private static int Elapsed(int current, int previous)
    {
        return unchecked(current - previous);
    }
}
