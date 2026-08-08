using System;

namespace Alacrity.PluginSdk;

/// Legacy compatibility contract for earlier Hitboxes package builds.
[Obsolete("Hitboxes now uses IPluginEntitySnapshotService and IPluginOverlayService. This compatibility contract is retained for binary compatibility only.")]
public interface IHitboxOverlaySettings
{
    /// Returns the current immutable presentation policy without exposing Terraria state.
    HitboxOverlaySettingsSnapshot GetSnapshot();
}

/// Legacy immutable Hitboxes presentation policy.
[Obsolete("Hitboxes now owns a generic overlay registration. This compatibility value remains only for earlier package builds.")]
public sealed class HitboxOverlaySettingsSnapshot
{
    /// Creates an immutable client-presentation hitbox policy.
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

    /// Whether player collision bounds are visible.
    public bool ShowPlayerHitboxes { get; }
    /// Whether NPC collision bounds are visible.
    public bool ShowNpcHitboxes { get; }
    /// Whether projectile collision bounds are visible.
    public bool ShowProjectileHitboxes { get; }
    /// Whether friendly projectile bounds are visible.
    public bool ShowFriendlyProjectiles { get; }
    /// Whether hostile projectile bounds are visible.
    public bool ShowHostileProjectiles { get; }
    /// Whether vanilla-computed melee swing bounds are visible.
    public bool ShowSwingHitboxes { get; }
    /// Outline color for player bounds.
    public PluginColor PlayerColor { get; }
    /// Outline color for NPC bounds.
    public PluginColor NpcColor { get; }
    /// Outline color for friendly projectile bounds.
    public PluginColor FriendlyProjectileColor { get; }
    /// Outline color for hostile projectile bounds.
    public PluginColor HostileProjectileColor { get; }
    /// Outline color for melee swing bounds.
    public PluginColor SwingColor { get; }

    /// Whether at least one overlay category can be drawn.
    public bool HasVisibleOverlays => ShowPlayerHitboxes || ShowNpcHitboxes || (ShowProjectileHitboxes && (ShowFriendlyProjectiles || ShowHostileProjectiles)) || ShowSwingHitboxes;
}
