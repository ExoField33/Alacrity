using System;

namespace Alacrity.PluginSdk;

/// <summary>Legacy compatibility contract for earlier Hitboxes package builds.</summary>
[Obsolete("Hitboxes now uses IPluginEntitySnapshotService and IPluginOverlayService. This compatibility contract is retained for binary compatibility only.")]
public interface IHitboxOverlaySettings
{
    /// <summary>Returns the current immutable presentation policy without exposing Terraria state.</summary>
    HitboxOverlaySettingsSnapshot GetSnapshot();
}

/// <summary>Legacy immutable Hitboxes presentation policy.</summary>
[Obsolete("Hitboxes now owns a generic overlay registration. This compatibility value remains only for earlier package builds.")]
public sealed class HitboxOverlaySettingsSnapshot
{
    /// <summary>Creates an immutable client-presentation hitbox policy.</summary>
    public HitboxOverlaySettingsSnapshot(bool showPlayerHitboxes, bool showNpcHitboxes, bool showProjectileHitboxes, bool showFriendlyProjectiles, bool showHostileProjectiles, bool showSwingHitboxes, PluginColor playerColor, PluginColor npcColor, PluginColor friendlyProjectileColor, PluginColor hostileProjectileColor, PluginColor swingColor)
    {
        ShowPlayerHitboxes = showPlayerHitboxes;
        ShowNpcHitboxes = showNpcHitboxes;
        ShowProjectileHitboxes = showProjectileHitboxes;
        ShowFriendlyProjectiles = showFriendlyProjectiles;
        ShowHostileProjectiles = showHostileProjectiles;
        ShowSwingHitboxes = showSwingHitboxes;
        PlayerColor = playerColor;
        NpcColor = npcColor;
        FriendlyProjectileColor = friendlyProjectileColor;
        HostileProjectileColor = hostileProjectileColor;
        SwingColor = swingColor;
    }

    /// <summary>Whether player collision bounds are visible.</summary>
    public bool ShowPlayerHitboxes { get; }
    /// <summary>Whether NPC collision bounds are visible.</summary>
    public bool ShowNpcHitboxes { get; }
    /// <summary>Whether projectile collision bounds are visible.</summary>
    public bool ShowProjectileHitboxes { get; }
    /// <summary>Whether friendly projectile bounds are visible.</summary>
    public bool ShowFriendlyProjectiles { get; }
    /// <summary>Whether hostile projectile bounds are visible.</summary>
    public bool ShowHostileProjectiles { get; }
    /// <summary>Whether vanilla-computed melee swing bounds are visible.</summary>
    public bool ShowSwingHitboxes { get; }
    /// <summary>Outline color for player bounds.</summary>
    public PluginColor PlayerColor { get; }
    /// <summary>Outline color for NPC bounds.</summary>
    public PluginColor NpcColor { get; }
    /// <summary>Outline color for friendly projectile bounds.</summary>
    public PluginColor FriendlyProjectileColor { get; }
    /// <summary>Outline color for hostile projectile bounds.</summary>
    public PluginColor HostileProjectileColor { get; }
    /// <summary>Outline color for melee swing bounds.</summary>
    public PluginColor SwingColor { get; }

    /// <summary>Whether at least one overlay category can be drawn.</summary>
    public bool HasVisibleOverlays => ShowPlayerHitboxes || ShowNpcHitboxes || (ShowProjectileHitboxes && (ShowFriendlyProjectiles || ShowHostileProjectiles)) || ShowSwingHitboxes;
}
