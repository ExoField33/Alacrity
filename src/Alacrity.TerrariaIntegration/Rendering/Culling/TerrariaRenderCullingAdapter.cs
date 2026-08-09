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
    private readonly TerrariaRenderCullingBounds cameraBounds = new TerrariaRenderCullingBounds();

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

    private bool IsVisible(Rectangle bounds, int margin)
    {
        return IsVisible(new Vector2(bounds.X, bounds.Y), bounds.Width, bounds.Height, margin);
    }

    private bool IsVisible(Vector2 position, int width, int height, int margin)
    {
        EnsureCameraBounds();
        return cameraBounds.IsVisible(position.X, position.Y, width, height, margin);
    }

    /// <summary>
    /// Camera position and scaled size change much less often than individual render candidates.
    /// Keep the normalized pixel rectangle until either input changes; per-entity checks then only
    /// perform their own bounds conversion and four comparisons.
    /// </summary>
    private void EnsureCameraBounds()
    {
        Vector2 position = Main.Camera.ScaledPosition;
        Vector2 size = Main.Camera.ScaledSize;
        cameraBounds.Update(position.X, position.Y, size.X, size.Y);
    }
}
