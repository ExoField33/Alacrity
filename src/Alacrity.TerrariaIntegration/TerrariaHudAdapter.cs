using System;
using System.Diagnostics;
using System.Reflection;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Renderers;

namespace AlacrityTerraria;

/// <summary>Terraria-owned renderer for generic gameplay HUD widgets.</summary>
internal sealed class TerrariaHudAdapter : IPluginHudRenderer
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private readonly PluginHudHost host;
    private readonly TerrariaHudCanvas canvas = new TerrariaHudCanvas();

    internal TerrariaHudAdapter(PluginHudHost host) { this.host = host ?? throw new ArgumentNullException(nameof(host)); }

    internal void Draw(SpriteBatch spriteBatch)
    {
        if (spriteBatch == null || Main.gameMenu) return;
        float scale = Main.UIScale;
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) scale = 1f;
        host.Dispatch(this, new PluginHudFrame(Main.screenWidth, Main.screenHeight, scale, Clock.Elapsed, Main.GameUpdateCount));
    }

    public void Render(PluginId owner, PluginHudWidgetDescriptor descriptor, Action<IPluginHudCanvas, PluginHudFrame> draw, PluginHudFrame frame)
    {
        canvas.Bind(owner, Main.spriteBatch);
        draw(canvas, frame);
    }
}

/// <summary>Translates host-neutral HUD commands into Terraria UI drawing while retaining widget ownership.</summary>
internal sealed class TerrariaHudCanvas : IPluginHudCanvas
{
    private readonly TerrariaPlayerAvatarRenderer avatars = new TerrariaPlayerAvatarRenderer();
    private PluginId owner;
    private SpriteBatch spriteBatch;

    internal void Bind(PluginId owner, SpriteBatch spriteBatch) { this.owner = owner; this.spriteBatch = spriteBatch; }

    public void DrawPanel(PluginUiRect bounds, PluginOverlayColor color)
    {
        if (spriteBatch == null || bounds.Width <= 0f || bounds.Height <= 0f) return;
        Rectangle rectangle = ToRectangle(bounds);
        Utils.DrawInvBG(spriteBatch, rectangle, ToColor(color));
    }

    public void DrawText(string text, float x, float y, PluginOverlayColor color, float scale = 1f, float originX = 0f, float originY = 0f)
    {
        if (spriteBatch == null || string.IsNullOrEmpty(text)) return;
        Utils.DrawBorderString(spriteBatch, text, new Vector2(x, y), ToColor(color), scale, originX, originY, -1);
    }

    public void DrawAsset(string approvedAssetId, PluginUiRect bounds, PluginOverlayColor? tint = null)
    {
        if (spriteBatch == null || string.IsNullOrWhiteSpace(approvedAssetId)) return;
        try { spriteBatch.Draw(PluginUiRuntime.RequestApprovedTexture(approvedAssetId), ToRectangle(bounds), tint.HasValue ? ToColor(tint.Value) : Color.White); }
        catch { }
    }

    public void DrawPlayerAvatar(int playerId, float x, float y, float scale = 1f)
    {
        if (spriteBatch == null || playerId < 0 || Main.player == null || playerId >= Main.player.Length) return;
        avatars.DrawPlayer(spriteBatch, Main.player[playerId], new Vector2(x, y), scale);
    }

    public void DrawNpcHead(int npcType, float x, float y, float scale = 1f, PluginOverlayColor? tint = null)
    {
        if (spriteBatch == null) return;
        avatars.DrawNpcHead(spriteBatch, npcType, new Vector2(x, y), scale, tint.HasValue ? ToColor(tint.Value) : Color.White);
    }

    public void DrawInteractiveAsset(string interactionId, string approvedAssetId, PluginUiRect bounds)
    {
        PluginIconInteractionState state = PluginUiRuntime.EvaluateIconInteraction(owner, interactionId, bounds);
        if (!state.IsRegistered) return;
        DrawAsset(approvedAssetId, Scale(bounds, state.Scale), state.Color.HasValue ? new PluginOverlayColor(state.Color.Value.Red, state.Color.Value.Green, state.Color.Value.Blue) : (PluginOverlayColor?)null);
        FinishInteraction(interactionId, bounds, state);
    }

    public void DrawInteractiveNpcHead(string interactionId, int npcType, PluginUiRect bounds)
    {
        PluginIconInteractionState state = PluginUiRuntime.EvaluateIconInteraction(owner, interactionId, bounds);
        if (!state.IsRegistered) return;
        DrawNpcHead(npcType, bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f, 0.66f * state.Scale, state.Color.HasValue ? new PluginOverlayColor(state.Color.Value.Red, state.Color.Value.Green, state.Color.Value.Blue) : (PluginOverlayColor?)null);
        FinishInteraction(interactionId, bounds, state);
    }

    public bool CapturePointer(PluginUiRect bounds)
    {
        bool contains = bounds.Contains(Main.mouseX, Main.mouseY);
        if (contains && Main.LocalPlayer != null) Main.LocalPlayer.mouseInterface = true;
        return contains;
    }

    private void FinishInteraction(string id, PluginUiRect bounds, PluginIconInteractionState state)
    {
        if (!state.IsHovered) return;
        CapturePointer(bounds);
        PluginUiRuntime.DrawIconTooltip(spriteBatch, state);
        PluginUiRuntime.TryActivateIconInteraction(owner, id, bounds);
    }

    private static PluginUiRect Scale(PluginUiRect bounds, float scale)
    {
        if (scale <= 1f) return bounds;
        float width = bounds.Width * scale; float height = bounds.Height * scale;
        return new PluginUiRect(bounds.X + (bounds.Width - width) / 2f, bounds.Y + (bounds.Height - height) / 2f, width, height);
    }
    private static Rectangle ToRectangle(PluginUiRect bounds) => new Rectangle((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y), Math.Max(1, (int)Math.Round(bounds.Width)), Math.Max(1, (int)Math.Round(bounds.Height)));
    private static Color ToColor(PluginOverlayColor color) => new Color(color.Red, color.Green, color.Blue, color.Alpha);
}

/// <summary>Caches optional Terraria avatar renderer lookups for every HUD widget, not one plugin.</summary>
internal sealed class TerrariaPlayerAvatarRenderer
{
    private bool attempted;
    private IPlayerRenderer playerRenderer;
    private MethodInfo npcHeadIndex;
    private FieldInfo npcHeads;
    private FieldInfo ghost;
    private PropertyInfo assetValue;

    internal void DrawPlayer(SpriteBatch spriteBatch, Player player, Vector2 position, float scale)
    {
        if (player == null || player.dead) return;
        Ensure();
        try
        {
            if (player.ghost)
            {
                Texture2D texture = GetTexture(ghost?.GetValue(null));
                if (texture == null) return;
                int frameHeight = texture.Height / 4;
                spriteBatch.Draw(texture, position, new Rectangle(0, frameHeight * (player.ghostFrame % 4), texture.Width, frameHeight), Color.White, 0f, new Vector2(texture.Width / 2f, frameHeight / 2f), 0.42f * scale, SpriteEffects.None, 0f);
                return;
            }
            playerRenderer?.DrawPlayerHead(Main.Camera, player, position + new Vector2(0f, -2f * scale), 1f, 0.55f * 1.15f * scale, Color.Transparent);
        }
        catch { }
    }

    internal void DrawNpcHead(SpriteBatch spriteBatch, int npcType, Vector2 position, float scale, Color color)
    {
        Ensure();
        try
        {
            if (npcHeadIndex == null || npcHeads == null) return;
            int index = (int)npcHeadIndex.Invoke(null, new object[] { npcType });
            Texture2D texture = GetTexture((npcHeads.GetValue(null) as Array)?.GetValue(index));
            if (texture != null) spriteBatch.Draw(texture, position, null, color, 0f, new Vector2(texture.Width / 2f, texture.Height / 2f), scale, SpriteEffects.None, 0f);
        }
        catch { }
    }

    private void Ensure()
    {
        if (attempted) return;
        attempted = true;
        playerRenderer = Main.PlayerRenderer;
        npcHeadIndex = typeof(NPC).GetMethod("TypeToDefaultHeadIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int) }, null);
        npcHeads = typeof(TextureAssets).GetField("NpcHead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        ghost = typeof(TextureAssets).GetField("Ghost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }

    private Texture2D GetTexture(object asset)
    {
        if (asset == null) return null;
        assetValue ??= asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        return assetValue?.GetValue(asset, null) as Texture2D;
    }
}
