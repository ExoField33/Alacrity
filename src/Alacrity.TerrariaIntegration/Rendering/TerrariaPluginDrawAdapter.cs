using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AlacrityTerraria.Rendering;

/// <summary>
/// Owns generic plugin drawing at Terraria's verified draw phases. The static bridge only forwards
/// version-locked entry points here; no bundled plugin identity participates in this dispatch.
/// </summary>
internal sealed class TerrariaPluginDrawAdapter : IDisposable
{
    private readonly PluginNotificationCenter notifications;
    private readonly PluginOverlayHost overlays;
    private readonly PluginHudHost hud;
    private readonly TerrariaHudAdapter hudAdapter;
    private readonly TerrariaEntitySnapshotCache entitySnapshots;
    private readonly Action<string, Exception> reportFailure;
    private readonly TerrariaOverlayAdapter overlayAdapter = new TerrariaOverlayAdapter();
    private bool projectionVerified;
    private bool projectionAvailable = true;

    internal TerrariaPluginDrawAdapter(
        PluginNotificationCenter notifications,
        PluginOverlayHost overlays,
        PluginHudHost hud,
        TerrariaHudAdapter hudAdapter,
        TerrariaEntitySnapshotCache entitySnapshots,
        Action<string, Exception> reportFailure)
    {
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        this.overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
        this.hud = hud ?? throw new ArgumentNullException(nameof(hud));
        this.hudAdapter = hudAdapter ?? throw new ArgumentNullException(nameof(hudAdapter));
        this.entitySnapshots = entitySnapshots ?? throw new ArgumentNullException(nameof(entitySnapshots));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal void DrawNotifications(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null) return;
        int y = 96;
        foreach (PluginNotification notification in notifications.GetActive(DateTimeOffset.UtcNow))
        {
            if ((notification.Options.Target & PluginNotificationTarget.InGame) == 0)
                continue;
            PluginColor color = notification.Options.Color ?? new PluginColor(Color.LightGoldenrodYellow.R, Color.LightGoldenrodYellow.G, Color.LightGoldenrodYellow.B);
            Utils.DrawBorderString(spriteBatch, notification.Message, new Vector2(Main.screenWidth - 18, y), new Color(color.Red, color.Green, color.Blue), 0.72f, 1f, 0f, -1);
            y += 24;
        }
        DrawHudOverlays(spriteBatch);
    }

    internal void DrawWorldOverlays(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null)
        {
            return;
        }

        if (Main.gameMenu)
        {
            ResetProjectionVerification();
            return;
        }

        if (!overlays.HasRegistrations(PluginOverlaySpace.World) || !VerifyLiveProjection())
        {
            return;
        }

        try
        {
            entitySnapshots.RefreshLocalPlayerPresentation();
            overlayAdapter.Dispatch(spriteBatch, overlays, PluginOverlaySpace.World);
        }
        catch (Exception exception) { reportFailure("Plugin world overlays", exception); }
    }

    internal void DrawHudOverlays(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null || Main.gameMenu || !overlays.HasRegistrations(PluginOverlaySpace.Hud)) return;
        try { overlayAdapter.Dispatch(spriteBatch, overlays, PluginOverlaySpace.Hud); }
        catch (Exception exception) { reportFailure("Plugin HUD overlays", exception); }
    }

    internal void DrawMenuOverlays(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null || !Main.gameMenu) return;
        ResetProjectionVerification();
        if (!overlays.HasRegistrations(PluginOverlaySpace.Menu)) return;
        try { overlayAdapter.Dispatch(spriteBatch, overlays, PluginOverlaySpace.Menu); }
        catch (Exception exception) { reportFailure("Plugin menu overlays", exception); }
    }

    internal void DrawHudWidgets(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null || Main.gameMenu || !hud.HasRegistrations()) return;
        try { hudAdapter.Draw(spriteBatch); }
        catch (Exception exception) { reportFailure("Plugin HUD draw", exception); }
    }

    public void Dispose() => overlayAdapter.Dispose();

    private bool VerifyLiveProjection()
    {
        if (projectionVerified)
        {
            return projectionAvailable;
        }

        projectionVerified = true;
        if (!TerrariaWorldProjection.TryVerifyLiveState(out string diagnostic))
        {
            projectionAvailable = false;
            reportFailure("Plugin world-overlay projection", new InvalidOperationException(diagnostic));
        }

        return projectionAvailable;
    }

    private void ResetProjectionVerification()
    {
        projectionVerified = false;
        projectionAvailable = true;
    }
}
