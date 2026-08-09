using System;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Graphics.Renderers;

namespace AlacrityTerraria.Rendering.Culling;

/// <summary>
/// Applies the effective generic culling policy at verified Terraria world-render sites. The
/// bounds are deliberately expanded to protect large player effects, item glows, and trails.
/// </summary>
internal sealed class TerrariaRenderCullingAdapter
{
    private readonly PluginRenderCullingHost policies;
    private readonly Action<string, Exception> reportFailure;
    private PluginRenderCullingCategory categories;

    internal TerrariaRenderCullingAdapter(PluginRenderCullingHost policies, Action<string, Exception> reportFailure)
    {
        this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal bool ShouldDrawPlayer(Player player)
    {
        if ((categories & PluginRenderCullingCategory.Players) == 0 || player == null || player.whoAmI == Main.myPlayer)
        {
            return true;
        }

        return IsVisible(player.Hitbox, 320);
    }

    internal bool ShouldDrawDroppedItem(int itemIndex)
    {
        if ((categories & PluginRenderCullingCategory.DroppedItems) == 0 || itemIndex < 0 || itemIndex >= Main.item.Length)
        {
            return true;
        }

        WorldItem item = Main.item[itemIndex];
        return item == null || !item.active || IsVisible(item.Hitbox, 128);
    }

    internal bool ShouldDrawDust(Dust dust)
    {
        if ((categories & PluginRenderCullingCategory.Dust) == 0 || dust == null)
        {
            return true;
        }

        return IsVisible(dust.position, 4, 4, 192);
    }

    internal bool ShouldDrawWorldParticle(ParticleRenderer renderer, IParticle particle)
    {
        if ((categories & PluginRenderCullingCategory.WorldParticles) == 0 ||
            (renderer != Main.ParticleSystem_World_BehindPlayers && renderer != Main.ParticleSystem_World_OverPlayers) ||
            !(particle is ABasicParticle basicParticle))
        {
            return true;
        }

        Vector2 position = renderer.Settings.AnchorPosition + basicParticle.LocalPosition;
        return IsVisible(position, 1, 1, 192);
    }

    internal void Refresh()
    {
        try
        {
            categories = policies.GetEffectiveCategories();
        }
        catch (Exception exception)
        {
            reportFailure("Render-culling policy", exception);
            categories = PluginRenderCullingCategory.None;
        }
    }

    private static bool IsVisible(Rectangle bounds, int margin)
    {
        return IsVisible(new Vector2(bounds.X, bounds.Y), bounds.Width, bounds.Height, margin);
    }

    private static bool IsVisible(Vector2 position, int width, int height, int margin)
    {
        Vector2 cameraPosition = Main.Camera.ScaledPosition;
        Vector2 cameraSize = Main.Camera.ScaledSize;
        int left = (int)Math.Floor(cameraPosition.X) - margin;
        int top = (int)Math.Floor(cameraPosition.Y) - margin;
        int right = (int)Math.Ceiling(cameraPosition.X + cameraSize.X) + margin;
        int bottom = (int)Math.Ceiling(cameraPosition.Y + cameraSize.Y) + margin;
        int entityLeft = (int)Math.Floor(position.X);
        int entityTop = (int)Math.Floor(position.Y);
        int entityRight = entityLeft + Math.Max(1, width);
        int entityBottom = entityTop + Math.Max(1, height);

        return entityRight >= left && entityLeft <= right && entityBottom >= top && entityTop <= bottom;
    }
}
