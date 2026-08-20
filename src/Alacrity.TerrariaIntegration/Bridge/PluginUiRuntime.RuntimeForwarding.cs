using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Alacrity.PluginSdk;
using AlacrityTerraria.UserInterface.Banners;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Renderers;
using Terraria.UI.Gamepad;

namespace AlacrityTerraria
{
    // The injected entry point source-links the framework-neutral handshake model and never loads Core.
    // Every reflected member is exact-signature checked and cached so unavailable bridge code falls back to Terraria.
    public static partial class PluginUiRuntime
    {
        private const int PluginMenuMode = 888;
        private const int IngamePluginsCategory = 777016;
        private static readonly PluginUiRuntimeBridgeState State = new PluginUiRuntimeBridgeState();
        private static readonly BannerSearchPresenter BannerSearch = new BannerSearchPresenter();

        /// <summary>Latest bridge availability or failure diagnostic for support and crash reports.</summary>
        public static string LastBridgeDiagnostic { get { return State.LastDiagnostic ?? string.Empty; } }

        /// <summary>Returns the cached diagnostic for one independently resolved bridge capability.</summary>
        public static string GetBridgeCapabilityDiagnostic(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
                throw new ArgumentException("A bridge capability name is required.", nameof(capability));
            lock (State.CapabilityDiagnosticGate)
                return State.CapabilityDiagnostics.TryGetValue(capability, out var diagnostic) ? diagnostic : string.Empty;
        }

        public static bool HandleInput()
        {
            try
            {
                if (State.SuppressNextIngameOptionsClose)
                {
                    State.SuppressNextIngameOptionsClose = false;
                    return false;
                }

                FieldInfo menuMode;
                if (!TryGetMenuModeField(out menuMode))
                    return true;

                bool inIngamePluginsCategory = IsIngamePluginsCategory();
                if (ReadMenuMode(menuMode) != PluginMenuMode && !inIngamePluginsCategory)
                {
                    return true;
                }

                bool continueVanillaInput = HandlePluginMenuInput();
                if (!continueVanillaInput)
                {
                    ConsumeCurrentEscapeKey();
                    if (inIngamePluginsCategory)
                    {
                        State.SuppressNextIngameOptionsClose = true;
                    }
                    else
                    {
                        // MenuUI processes its back command after this input hook. Lock that
                        // native command for the same frame after the bridge has navigated one
                        // Alacrity-owned page, rather than letting it immediately close mode 888.
                        UILinkPointNavigator.Shortcuts.BackButtonLock = true;
                    }
                }

                return continueVanillaInput;
            }
            catch (Exception exception)
            {
                RecordFailure("Plugin-menu input", exception);
                FieldInfo menuMode;
                if (TryGetMenuModeField(out menuMode))
                    SetMenuMode(menuMode, 0);
                return true;
            }
        }

        /// <summary>
        /// Filters one native banner-claiming entry. The list itself remains entirely native; this
        /// presentation predicate only considers entries Terraria has already made claimable.
        /// </summary>
        public static bool ShouldDisplayAvailableBanner(int bannerIndex)
        {
            try
            {
                return BannerSearch.MatchesAvailableBanner(bannerIndex);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Draws the native banner-claiming search field within Terraria's existing inventory batch.</summary>
        public static void DrawAvailableBannerSearch(SpriteBatch spriteBatch, int x, int y)
        {
            try
            {
                BannerSearch.Draw(spriteBatch, x, y);
            }
            catch
            {
                // The native banner-claiming window must remain available when presentation fails.
            }
        }

        /// <summary>Keeps the banner menu available while its local filter has no matches.</summary>
        public static bool ShouldKeepBannerMenuAvailable()
        {
            try
            {
                return BannerSearch.HasActiveFilter;
            }
            catch
            {
                return false;
            }
        }

        private static void ConsumeCurrentEscapeKey()
        {
            // Keep the live state intact. Replacing it would make a held Escape appear as a
            // fresh press on the next update and immediately consume the next navigation level.
            // Advancing the previous snapshot consumes only this edge for native UI code.
            Main.oldKeyState = Main.keyState;
            Main.inputTextEscape = false;
            Main.keyCount = 0;
        }

        /// <summary>Version-locked startup entry point. It is safe to call more than once.</summary>
        public static void BootstrapPluginRuntime()
        {
            try
            {
                if (!EnsureRuntimeCapabilities()) return;
                if (!State.ShutdownHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => ShutdownPluginRuntime();
                    State.ShutdownHooked = true;
                }
                State.BootstrapPluginRuntime?.Invoke();
            }
            catch (Exception exception) { RecordFailure("Plugin runtime startup", exception); }
        }

        private static void ShutdownPluginRuntime()
        {
            try
            {
                State.ShutdownPluginRuntime?.Invoke();
            }
            catch (Exception exception) { RecordFailure("Plugin runtime shutdown", exception); }
        }

        public static void OpenPluginManager()
        {
            try
            {
                BootstrapPluginRuntime();
                if (!EnsureBridge())
                    return;

                SoundEngine.PlaySound(10, -1, -1, 1, 1f, 0f);
                State.Open();
            }
            catch (Exception exception)
            {
                RecordFailure("Open plugin manager", exception);
            }
        }

        public static void DrawAlacrityVersion(Color color, float verticalOffset, string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText) || !EnsureVersionRenderer())
                return;

            try
            {
                string originalVersion = (string)State.VersionNumber.GetValue(null);
                try
                {
                    State.VersionNumber.SetValue(null, versionText);
                    State.DrawVersionNumber(color, verticalOffset);
                    if (State.RuntimeCapabilitiesResolved)
                        State.DrawMenuOverlays?.Invoke(Main.spriteBatch);
                }
                finally
                {
                    State.VersionNumber.SetValue(null, originalVersion);
                }
            }
            catch (Exception exception)
            {
                RecordFailure("Draw Alacrity version", exception);
            }
        }

        public static void OpenIngamePluginSettings()
        {
            SetIngamePluginsCategory();
            try
            {
                if (!EnsureBridge() || State.OpenIngamePluginSettings == null)
                {
                    RestoreIngameOptionsCategory();
                    return;
                }

                State.OpenIngamePluginSettings();
            }
            catch (Exception exception)
            {
                RecordFailure("Open in-game plugin settings", exception);
                RestoreIngameOptionsCategory();
            }
        }

        public static void DrawIngamePluginSettings(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || !IsIngamePluginsCategory())
                return;

            try
            {
                if (!EnsureBridge() || State.DrawIngamePluginSettings == null)
                {
                    RestoreIngameOptionsCategory();
                    return;
                }

                State.DrawIngamePluginSettings(spriteBatch);
            }
            catch (Exception exception)
            {
                RecordFailure("Draw in-game plugin settings", exception);
                RestoreIngameOptionsCategory();
            }
        }

        /// <summary>Draws transient Core notifications at the established gameplay UI boundary.</summary>
        public static void DrawNotifications(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null)
                return;

            try
            {
                if (EnsureRuntimeCapabilities())
                {
                    if (!Main.gameMenu)
                    {
                        EnsureNotificationCapability();
                        State.DrawNotifications?.Invoke(spriteBatch);
                        State.DrawHudWidgets?.Invoke(spriteBatch);
                    }
                }
            }
            catch (Exception exception)
            {
                RecordFailure("Draw plugin notifications", exception);
            }
        }

        /// <summary>Version-locked input/update entry point. It is intentionally separate from drawing.</summary>
        public static void UpdatePluginKeybinds()
        {
            try
            {
                BootstrapPluginRuntime();
                if (EnsureRuntimeCapabilities())
                    State.UpdatePluginKeybinds?.Invoke();
            }
            catch (Exception exception)
            {
                RecordFailure("Update plugin keybinds", exception);
            }
        }

        /// <summary>Compatibility forward for the existing version-locked Hitboxes hook.</summary>
        public static void DrawHitboxes(SpriteBatch spriteBatch)
        {
            DrawWorldOverlays(spriteBatch);
        }

        /// <summary>Forwards the verified world draw phase to the generic host-owned overlay renderer.</summary>
        public static void DrawWorldOverlays(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || Main.gameMenu)
                return;
            try
            {
                if (!EnsureRuntimeCapabilities() || State.DrawWorldOverlays == null)
                    return;
                State.DrawWorldOverlays(spriteBatch);
            }
            catch (Exception exception)
            {
                RecordFailure("Draw world overlays", exception);
            }
        }

        /// <summary>Receives a vanilla-computed melee hitbox only when the optional diagnostics bridge is available.</summary>
        public static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            CaptureMeleeCollisionBounds(player, dontAttack, hitbox);
        }

        /// <summary>Forwards host-computed melee collision bounds to generic presentation consumers.</summary>
        public static void CaptureMeleeCollisionBounds(Player player, bool dontAttack, Rectangle hitbox)
        {
            try
            {
                // This is called from a combat-hot path. Once resolved, avoid even the bridge readiness check.
                Action<Player, bool, Rectangle> capture = State.CaptureMeleeCollisionBounds;
                if (capture != null)
                    capture(player, dontAttack, hitbox);
                else if (EnsureRuntimeCapabilities())
                    State.CaptureMeleeCollisionBounds?.Invoke(player, dontAttack, hitbox);
            }
            catch (Exception exception)
            {
                RecordFailure("Capture melee collision bounds", exception);
            }
        }

        /// <summary>Runs before Terraria copies native key states so plugin trigger IDs exist in both old and current sets.</summary>
        public static void EnsurePluginKeybindStateShape()
        {
            try
            {
                BootstrapPluginRuntime();
                if (EnsureRuntimeCapabilities())
                    State.EnsurePluginKeybindStateShape?.Invoke();
            }
            catch (Exception exception)
            {
                RecordFailure("Synchronize plugin keybind state", exception);
            }
        }

        // These version-locked calls are in per-instance draw/update loops. Delegate resolution is
        // lazy, but once a capability is present the hot path is one cached delegate invocation.
        // Every unavailable or faulted path fails open to unchanged Terraria behavior.
        public static bool ShouldRunDustSystem()
        {
            Func<bool> callback = State.ShouldRunDustSystem;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldRunDustSystem;
            return InvokeGate(callback, "Dust-system gate");
        }

        public static bool ShouldCreateDust(int dustType)
        {
            Func<int, bool> callback = State.ShouldCreateDust;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldCreateDust;
            return InvokeGate(callback, dustType, "Dust-creation gate");
        }

        public static bool ShouldUpdateDustInstance(Dust dust)
        {
            Func<Dust, bool> callback = State.ShouldUpdateDustInstance;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldUpdateDustInstance;
            return InvokeGate(callback, dust, "Dust-update gate");
        }

        public static bool ShouldDrawDustInstance(Dust dust)
        {
            Func<Dust, bool> callback = State.ShouldDrawDustInstance;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldDrawDustInstance;
            return InvokeGate(callback, dust, "Dust-draw gate");
        }

        public static bool ShouldDrawWorldPlayer(Player player)
        {
            Func<Player, bool> callback = State.ShouldDrawWorldPlayer;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldDrawWorldPlayer;
            return InvokeGate(callback, player, "World-player culling gate");
        }

        public static bool ShouldDrawWorldItem(int itemIndex)
        {
            Func<int, bool> callback = State.ShouldDrawWorldItem;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldDrawWorldItem;
            return InvokeGate(callback, itemIndex, "World-item culling gate");
        }

        public static bool ShouldDrawWorldParticle(ParticleRenderer renderer, IParticle particle)
        {
            Func<ParticleRenderer, IParticle, bool> callback = State.ShouldDrawWorldParticle;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldDrawWorldParticle;
            if (callback == null) return true;
            try { return callback(renderer, particle); }
            catch (Exception exception) { RecordFailure("World-particle culling gate", exception); return true; }
        }

        public static bool ShouldRunGoreSystem()
        {
            Func<bool> callback = State.ShouldRunGoreSystem;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldRunGoreSystem;
            return InvokeGate(callback, "Gore-system gate");
        }

        /// <summary>
        /// Leaves native Paladin shield presentation intact unless the managed runtime has an
        /// active local presentation suppression policy. A missing bridge always draws the icon.
        /// </summary>
        public static bool ShouldDrawPaladinShieldIcon()
        {
            Func<bool> callback = State.ShouldDrawPaladinShieldIcon;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.ShouldDrawPaladinShieldIcon;
            return callback == null || InvokeOptionalGate(callback, "Paladin shield icon presentation gate");
        }

        /// <summary>Returns whether the generic paint-preparation optimization policy is active.</summary>
        public static bool IsPaintPreparationOptimizationEnabled()
        {
            Func<bool> callback = State.IsPaintPreparationOptimizationEnabled;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsPaintPreparationOptimizationEnabled;
            return callback != null && InvokeOptionalGate(callback, "Paint-preparation optimization gate");
        }

        /// <summary>Preserves vanilla extra preparation whenever the optional optimization is unavailable.</summary>
        public static bool IsPaintExtraPreparationRelevant(int tileType)
        {
            Func<int, bool> callback = State.IsPaintExtraPreparationRelevant;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsPaintExtraPreparationRelevant;
            return callback == null || InvokeOptionalGate(callback, tileType, "Paint extra-preparation gate");
        }

        /// <summary>Returns whether the generic clothing-entity presentation policy is active.</summary>
        public static bool IsClothingEntityPresentationOptimizationEnabled()
        {
            Func<bool> callback = State.IsClothingEntityPresentationOptimizationEnabled;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsClothingEntityPresentationOptimizationEnabled;
            return callback != null && InvokeOptionalGate(callback, "Clothing-entity presentation optimization gate");
        }

        /// <summary>Starts the optional bounded cold-work frame for clothing entities.</summary>
        public static void BeginClothingEntityPreparationFrame()
        {
            Action callback = State.BeginClothingEntityPreparationFrame;
            if (callback == null && EnsureRuntimeCapabilities())
            {
                callback = State.BeginClothingEntityPreparationFrame;
            }

            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                RecordFailure("Clothing-entity preparation frame", exception);
            }
        }

        /// <summary>
        /// Keeps native clothing drawing fail-open when the optional cold-work coordinator is
        /// unavailable or faulted.
        /// </summary>
        public static bool TryBeginClothingEntityPreparation(int entityKind, long visualConfiguration)
        {
            Func<int, long, bool> callback = State.TryBeginClothingEntityPreparation;
            if (callback == null && EnsureRuntimeCapabilities())
            {
                callback = State.TryBeginClothingEntityPreparation;
            }

            return callback == null || InvokeOptionalGate(
                callback,
                entityKind,
                visualConfiguration,
                "Clothing-entity preparation admission");
        }

        /// <summary>Completes one admitted native clothing draw without affecting vanilla fallback.</summary>
        public static void CompleteClothingEntityPreparation(int entityKind, long visualConfiguration)
        {
            Action<int, long> callback = State.CompleteClothingEntityPreparation;
            if (callback == null && EnsureRuntimeCapabilities())
            {
                callback = State.CompleteClothingEntityPreparation;
            }

            if (callback == null)
            {
                return;
            }

            try
            {
                callback(entityKind, visualConfiguration);
            }
            catch (Exception exception)
            {
                RecordFailure("Clothing-entity preparation completion", exception);
            }
        }

        /// <summary>Returns whether the generic waterfall presentation policy is active.</summary>
        public static bool IsWaterfallPresentationOptimizationEnabled()
        {
            Func<bool> callback = State.IsWaterfallPresentationOptimizationEnabled;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsWaterfallPresentationOptimizationEnabled;
            return callback != null && InvokeOptionalGate(callback, "Waterfall presentation optimization gate");
        }

        /// <summary>Returns whether the generic TileDrawing presentation policy is active.</summary>
        public static bool IsTileDrawingPresentationOptimizationEnabled()
        {
            Func<bool> callback = State.IsTileDrawingPresentationOptimizationEnabled;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsTileDrawingPresentationOptimizationEnabled;
            return callback != null && InvokeOptionalGate(callback, "TileDrawing presentation optimization gate");
        }

        /// <summary>Returns whether the generic top-level draw-orchestration policy is active.</summary>
        public static bool IsDrawOrchestrationOptimizationEnabled()
        {
            Func<bool> callback = State.IsDrawOrchestrationOptimizationEnabled;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.IsDrawOrchestrationOptimizationEnabled;
            return callback != null && InvokeOptionalGate(callback, "Draw-orchestration optimization gate");
        }

        /// <summary>
        /// Gives the host one opportunity to draw the optimized laser-ruler presentation. False
        /// always means the patched method must continue into Terraria's native renderer.
        /// </summary>
        public static bool TryDrawLaserRulerPresentation()
        {
            Func<bool> callback = State.TryDrawLaserRulerPresentation;
            if (callback == null && EnsureRuntimeCapabilities()) callback = State.TryDrawLaserRulerPresentation;
            return callback != null && InvokeOptionalGate(callback, "Laser-ruler presentation optimization");
        }

        /// <summary>Begins a version-locked optional rain pass, otherwise native drawing continues.</summary>
        public static bool TryBeginRainPresentation(bool useWorldTransform)
        {
            if (!EnsureRainPresentationCapabilities())
            {
                return false;
            }

            try
            {
                return State.TryBeginRainPresentation(useWorldTransform);
            }
            catch (Exception exception)
            {
                RecordFailure("Rain presentation bridge", exception);
                return false;
            }
        }

        /// <summary>Queues one native active rain entry for the already-started optional pass.</summary>
        public static bool TryQueueRainPresentation(
            Texture2D texture,
            Vector2 position,
            Rectangle? source,
            Color color,
            float rotation,
            Vector2 origin,
            float scale,
            SpriteEffects effects,
            float layerDepth)
        {
            PluginUiRuntimeBridgeState.RainPresentationQueueDelegate callback = State.TryQueueRainPresentation;
            if (callback == null)
            {
                return false;
            }

            try
            {
                return callback(texture, position, source, color, rotation, origin, scale, effects, layerDepth);
            }
            catch (Exception exception)
            {
                RecordFailure("Queue rain presentation", exception);
                return true;
            }
        }

        /// <summary>Completes the optional rain pass and restores the native SpriteBatch state.</summary>
        public static void EndRainPresentation()
        {
            Action callback = State.EndRainPresentation;
            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                RecordFailure("Finish rain presentation", exception);
            }
        }

        /// <summary>
        /// Executes a version-locked lighting range through the active Core bridge. A missing,
        /// stale, or disabled bridge uses Terraria's native FastParallel implementation.
        /// </summary>
        public static bool TryRunLightingParallel(
            int fromInclusive,
            int toExclusive,
            Delegate callback,
            object context)
        {
            PluginUiRuntimeBridgeState.LightingParallelDelegate optimized = State.TryRunLightingParallel;
            if (optimized == null && EnsureRuntimeCapabilities())
            {
                optimized = State.TryRunLightingParallel;
            }

            if (optimized == null)
            {
                return false;
            }

            return optimized(fromInclusive, toExclusive, callback, context);
        }

        /// <summary>
        /// Allows the Core bridge to replace only audited, static TileDrawing entries. Returning
        /// false preserves Terraria's native DrawSingleTile call for every unsupported case.
        /// </summary>
        public static bool TryDrawStaticTileChunk(
            TileDrawing drawing,
            bool solidLayer,
            Vector2 screenPosition,
            Vector2 screenOffset,
            int tileX,
            int tileY)
        {
            PluginUiRuntimeBridgeState.StaticTileChunkDrawDelegate callback = State.TryDrawStaticTileChunk;
            if (callback == null && EnsureRuntimeCapabilities())
            {
                callback = State.TryDrawStaticTileChunk;
            }

            if (callback == null)
            {
                return false;
            }

            try
            {
                return callback(drawing, solidLayer, screenPosition, screenOffset, tileX, tileY);
            }
            catch (Exception exception)
            {
                RecordFailure("Static tile-chunk presentation optimization", exception);
                return false;
            }
        }


        /// <summary>Forwards a native tile mutation to the host-owned static descriptor cache.</summary>
        public static void InvalidateStaticTileChunks(int tileX, int tileY)
        {
            Action<int, int> callback = State.InvalidateStaticTileChunks;
            if (callback == null && EnsureRuntimeCapabilities())
            {
                callback = State.InvalidateStaticTileChunks;
            }

            if (callback == null)
            {
                return;
            }

            try
            {
                callback(tileX, tileY);
            }
            catch (Exception exception)
            {
                RecordFailure("Static tile-chunk invalidation", exception);
            }
        }

        private static bool InvokeGate(Func<bool> callback, string operation)
        {
            if (callback == null) return true;
            try { return callback(); }
            catch (Exception exception) { RecordFailure(operation, exception); return true; }
        }

        private static bool InvokeOptionalGate(Func<bool> callback, string operation)
        {
            try { return callback(); }
            catch (Exception exception) { RecordFailure(operation, exception); return false; }
        }

        private static bool InvokeGate<T>(Func<T, bool> callback, T value, string operation)
        {
            if (callback == null) return true;
            try { return callback(value); }
            catch (Exception exception) { RecordFailure(operation, exception); return true; }
        }

        private static bool InvokeOptionalGate<T>(Func<T, bool> callback, T value, string operation)
        {
            try { return callback(value); }
            catch (Exception exception) { RecordFailure(operation, exception); return true; }
        }

        private static bool InvokeOptionalGate<TFirst, TSecond>(
            Func<TFirst, TSecond, bool> callback,
            TFirst first,
            TSecond second,
            string operation)
        {
            try { return callback(first, second); }
            catch (Exception exception) { RecordFailure(operation, exception); return true; }
        }

        public static bool TryHandlePluginChatCommand(string text)
        {
            return EnsureRuntimeCapabilities() && State.TryHandlePluginChatCommand != null && State.TryHandlePluginChatCommand(text);
        }

        /// <summary>Version-locked controls-menu entry point. It remains a no-op when the optional bridge is unavailable.</summary>
        public static void AppendPluginKeybindControls(UIManageControls controls)
        {
            if (controls == null)
                return;

            try
            {
                BootstrapPluginRuntime();
                if (EnsureRuntimeCapabilities())
                    State.AppendPluginKeybindControls?.Invoke(controls);
            }
            catch (Exception exception)
            {
                RecordFailure("Append plugin keybind controls", exception);
            }
        }

        // These methods are called only from version-locked chat IL patches. They remain no-ops
        // when no generic chat extension is registered or the optional Core bridge is unavailable.
        public static bool IsBetterChatActive()
        {
            return HasChatInputEditors();
        }

        public static bool HasChatInputEditors()
        {
            return EnsureChatBridge() && State.HasChatInputEditors != null && State.HasChatInputEditors();
        }

        /// <summary>Returns whether a generic editor owns an action that Terraria would otherwise process first.</summary>
        public static bool ShouldHandleChatInputAction(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) && EnsureChatBridge() && State.ShouldHandleChatInputAction != null && State.ShouldHandleChatInputAction(actionId);
        }

        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            return ProcessChatInput(text, allowMultiLine);
        }

        public static string ProcessChatInput(string text, bool allowMultiLine)
        {
            return EnsureChatBridge() && State.ProcessChatInput != null ? State.ProcessChatInput(text, allowMultiLine) : text;
        }

        /// <summary>Forwards one accepted player-chat submission to generic activation-scoped editors.</summary>
        public static void RecordSubmittedChatInput(string text)
        {
            if (!string.IsNullOrEmpty(text) && EnsureChatBridge() && State.RecordSubmittedChatInput != null)
                State.RecordSubmittedChatInput(text);
        }

        public static bool TryDeferOutgoingChatMessage(string text)
        {
            return !string.IsNullOrEmpty(text) && EnsureChatBridge() && State.TryDeferOutgoingChatMessage != null && State.TryDeferOutgoingChatMessage(text);
        }

        public static string TakeReadyOutgoingChatMessage()
        {
            return EnsureChatBridge() && State.TakeReadyOutgoingChatMessage != null ? State.TakeReadyOutgoingChatMessage() : null;
        }

        public static bool HasReadyOutgoingChatMessage()
        {
            return EnsureChatBridge() && State.HasReadyOutgoingChatMessage != null && State.HasReadyOutgoingChatMessage();
        }

        public static void DrawChatActionStrip()
        {
            if (EnsureChatBridge() && State.DrawChatActionStrip != null)
                State.DrawChatActionStrip();
        }

        public static string FormatPlayerChatText(string text)
        {
            return HasChatInputEditors()
                ? FormatChatInputForDraw(text)
                : FormatNativePlayerChatText(text);
        }

        public static string FormatChatInputForDraw(string text)
        {
            if (EnsureChatBridge() && State.FormatChatInputForDraw != null)
                return State.FormatChatInputForDraw(text);
            return Main.instance != null && Main.instance.textBlinkerState == 1 ? (text ?? string.Empty) + "|" : text;
        }

        public static object DecorateStoredChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            return EnsureChatBridge() && State.DecorateStoredChatMessage != null ? State.DecorateStoredChatMessage(snippets, baseColor, originalMessage) : snippets;
        }

        public static string PrepareStoredChatMessageText(string originalMessage, object messageContainer)
        {
            return EnsureChatBridge() && State.PrepareStoredChatMessageText != null
                ? State.PrepareStoredChatMessageText(originalMessage, messageContainer)
                : originalMessage ?? string.Empty;
        }

        public static void BeginStoredChatMessageDecoration()
        {
            if (EnsureChatBridge() && State.BeginStoredChatMessageDecoration != null)
            {
                State.BeginStoredChatMessageDecoration();
            }
        }

        public static void BeginStoredChatMessageDecorationForContainer(object messageContainer)
        {
            if (EnsureChatBridge() && State.BeginStoredChatMessageDecorationForContainer != null)
            {
                State.BeginStoredChatMessageDecorationForContainer(messageContainer);
            }
        }

        public static void EndStoredChatMessageDecoration()
        {
            if (EnsureChatBridge() && State.EndStoredChatMessageDecoration != null)
            {
                State.EndStoredChatMessageDecoration();
            }
        }

        public static void RefreshStoredChatMessagePresentations()
        {
            if (EnsureChatBridge() && State.RefreshStoredChatMessagePresentations != null)
            {
                State.RefreshStoredChatMessagePresentations();
            }
        }

        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            return EnsureChatBridge() && State.ShouldDisplayNetworkChatMessage != null ? State.ShouldDisplayNetworkChatMessage(messageAuthor) : true;
        }

        public static bool ShouldDisplayLocalChatMessage()
        {
            return EnsureChatBridge() && State.ShouldDisplayLocalChatMessage != null ? State.ShouldDisplayLocalChatMessage() : true;
        }

        public static void HandleChatSnippetHover(object snippet)
        {
            if (EnsureChatBridge() && State.HandleChatSnippetHover != null)
                State.HandleChatSnippetHover(snippet);
        }

        public static bool HandleChatSnippetClick(object snippet)
        {
            return EnsureChatBridge() && State.HandleChatSnippetClick != null && State.HandleChatSnippetClick(snippet);
        }

        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            return EnsureChatBridge() && State.GetChatSnippetVisibleColor != null ? State.GetChatSnippetVisibleColor(snippet, color) : color;
        }

        public static void CopyChatSnippetContext(object source, object copy)
        {
            if (EnsureChatBridge() && State.CopyChatSnippetContext != null)
                State.CopyChatSnippetContext(source, copy);
        }

        private static bool EnsureVersionRenderer()
        {
            if (State.VersionRendererResolved)
                return State.DrawVersionNumber != null && State.VersionNumber != null;

            State.VersionRendererResolved = true;
            string diagnostic;
            MethodInfo renderer;
            if (!State.Reflection.TryResolveStaticField(typeof(Main), "versionNumber", typeof(string), out State.VersionNumber, out diagnostic) ||
                !State.Reflection.TryResolveStaticMethod(typeof(Main), "DrawVersionNumber", typeof(void), new[] { typeof(Color), typeof(float) }, out renderer, out diagnostic))
            {
                RecordUnavailable(diagnostic);
                return false;
            }

            Delegate callback;
            if (!State.Reflection.TryCreateDelegate(renderer, typeof(Action<Color, float>), out callback, out diagnostic))
            {
                RecordUnavailable(diagnostic);
                return false;
            }

            State.DrawVersionNumber = (Action<Color, float>)callback;
            return true;
        }

        private static bool EnsureBridge()
        {
            if (State.PluginManagerCapabilitiesResolved)
                return State.Open != null;

            if (!EnsureBridgeAssembly())
                return false;

            State.PluginManagerCapabilitiesResolved = true;
            try
            {
                Type bridgeType = State.BridgeType;
                string diagnostic;
                MethodInfo open;
                MethodInfo openIngame;
                MethodInfo drawIngame;
                MethodInfo handleInput;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "Open", typeof(void), Type.EmptyTypes, out open, out diagnostic) ||
                    !State.Reflection.TryResolveStaticMethod(bridgeType, "OpenIngamePluginSettings", typeof(void), Type.EmptyTypes, out openIngame, out diagnostic) ||
                    !State.Reflection.TryResolveStaticMethod(bridgeType, "DrawIngamePluginSettings", typeof(void), new[] { typeof(SpriteBatch) }, out drawIngame, out diagnostic) ||
                    !State.Reflection.TryResolveStaticMethod(bridgeType, "HandlePluginMenuInput", typeof(bool), Type.EmptyTypes, out handleInput, out diagnostic))
                {
                    SetCapabilityDiagnostic("plugin-manager", diagnostic);
                    RecordUnavailable(diagnostic);
                    ClearPluginManagerDelegates();
                    return false;
                }

                Delegate callback = null;
                if (!State.Reflection.TryCreateDelegate(open, typeof(Action), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                State.Open = (Action)callback;
                if (!State.Reflection.TryCreateDelegate(openIngame, typeof(Action), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                State.OpenIngamePluginSettings = (Action)callback;
                if (!State.Reflection.TryCreateDelegate(drawIngame, typeof(Action<SpriteBatch>), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                State.DrawIngamePluginSettings = (Action<SpriteBatch>)callback;
                if (!State.Reflection.TryCreateDelegate(handleInput, typeof(Func<bool>), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                State.HandlePluginMenuInput = (Func<bool>)callback;
                SetCapabilityDiagnostic("plugin-manager", string.Empty);
                return true;
            }
            catch (Exception exception)
            {
                ClearPluginManagerDelegates();
                SetCapabilityDiagnostic("plugin-manager", exception.GetType().Name + ": " + exception.Message);
                RecordFailure("Resolve plugin-manager bridge", exception);
                return false;
            }
        }

        private static bool EnsureBridgeAssembly()
        {
            if (State.BridgeType != null)
                return true;
            if (State.BridgeLoadAttempted)
                return false;

            State.BridgeLoadAttempted = true;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "Alacrity.PluginUiCoreBridge.dll");
            if (!ClientManifestIntegrity.TryValidate(AppDomain.CurrentDomain.BaseDirectory, "5|2|14|1.4.5.6", out string integrityDiagnostic))
            {
                RecordUnavailable("Client integrity check failed: " + integrityDiagnostic);
                return false;
            }
            if (!File.Exists(path))
            {
                RecordUnavailable("Unavailable: Alacrity.PluginUiCoreBridge.dll was not found at " + path + ".");
                return false;
            }

            try
            {
                State.BridgeAssembly = Assembly.LoadFrom(path);
                State.BridgeType = State.BridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", false);
                if (State.BridgeType == null)
                {
                    RecordUnavailable("Unavailable: the UI bridge does not contain AlacrityTerraria.PluginUiRuntime.");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                State.BridgeAssembly = null;
                State.BridgeType = null;
                RecordFailure("Load plugin bridge assembly", exception);
                return false;
            }
        }

        private static bool EnsureRuntimeCapabilities()
        {
            if (State.RuntimeCapabilitiesResolved)
                return State.BootstrapPluginRuntime != null;
            if (!EnsureBridgeAssembly())
                return false;

            State.RuntimeCapabilitiesResolved = true;
            try
            {
                string diagnostic;
                Delegate callback = null;
                MethodInfo bootstrap;
                MethodInfo shutdown;
                MethodInfo handshake;
                if (!State.Reflection.TryResolveStaticMethod(State.BridgeType, "GetBridgeHandshake", typeof(string), Type.EmptyTypes, out handshake, out diagnostic) ||
                    !State.Reflection.TryCreateDelegate(handshake, typeof(Func<string>), out callback, out diagnostic))
                {
                    RecordUnavailable("Bridge compatibility handshake is unavailable: " + diagnostic);
                    return false;
                }
                State.GetBridgeHandshake = (Func<string>)callback;
                if (!BridgeCompatibilityDescriptor.TryParse(State.GetBridgeHandshake(), out BridgeCompatibilityDescriptor descriptor, out diagnostic))
                {
                    RecordUnavailable("Bridge compatibility handshake is invalid: " + diagnostic + " Rebuild/copy Alacrity assemblies together.");
                    return false;
                }
                if (!descriptor.TryValidateAgainst(State.ExpectedBridgeCompatibility, out diagnostic))
                {
                    RecordUnavailable(diagnostic + " Rebuild/copy Alacrity assemblies together.");
                    return false;
                }
                if (!State.Reflection.TryResolveStaticMethod(State.BridgeType, "BootstrapPluginRuntime", typeof(void), Type.EmptyTypes, out bootstrap, out diagnostic) ||
                    !State.Reflection.TryCreateDelegate(bootstrap, typeof(Action), out callback, out diagnostic))
                {
                    RecordUnavailable(diagnostic);
                    return false;
                }
                State.BootstrapPluginRuntime = (Action)callback;
                if (State.Reflection.TryResolveStaticMethod(State.BridgeType, "ShutdownPluginRuntime", typeof(void), Type.EmptyTypes, out shutdown, out _) &&
                    State.Reflection.TryCreateDelegate(shutdown, typeof(Action), out callback, out _))
                    State.ShutdownPluginRuntime = (Action)callback;
                ResolveOptionalCapabilities(State.BridgeType);
                return true;
            }
            catch (Exception exception)
            {
                RecordFailure("Resolve runtime bridge capabilities", exception);
                return false;
            }
        }

        private static void ResolveOptionalCapabilities(Type bridgeType)
        {
            if (TryResolveOptionalCapability(bridgeType, "hud-widgets", "DrawHudWidgets", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out var callback)) State.DrawHudWidgets = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "world-overlays", "DrawWorldOverlays", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out callback)) State.DrawWorldOverlays = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "menu-overlays", "DrawMenuOverlays", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out callback)) State.DrawMenuOverlays = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "combat-collision-capture", "CaptureMeleeCollisionBounds", typeof(Action<Player, bool, Rectangle>), typeof(void), new[] { typeof(Player), typeof(bool), typeof(Rectangle) }, out callback)) State.CaptureMeleeCollisionBounds = (Action<Player, bool, Rectangle>)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-update", "UpdatePluginKeybinds", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) State.UpdatePluginKeybinds = (Action)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-state", "EnsurePluginKeybindStateShape", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) State.EnsurePluginKeybindStateShape = (Action)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-controls", "AppendPluginKeybindControls", typeof(Action<UIManageControls>), typeof(void), new[] { typeof(UIManageControls) }, out callback)) State.AppendPluginKeybindControls = (Action<UIManageControls>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-system", "ShouldRunDustSystem", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.ShouldRunDustSystem = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-create", "ShouldCreateDust", typeof(Func<int, bool>), typeof(bool), new[] { typeof(int) }, out callback)) State.ShouldCreateDust = (Func<int, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-update", "ShouldUpdateDustInstance", typeof(Func<Dust, bool>), typeof(bool), new[] { typeof(Dust) }, out callback)) State.ShouldUpdateDustInstance = (Func<Dust, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-draw", "ShouldDrawDustInstance", typeof(Func<Dust, bool>), typeof(bool), new[] { typeof(Dust) }, out callback)) State.ShouldDrawDustInstance = (Func<Dust, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "world-player-culling", "ShouldDrawWorldPlayer", typeof(Func<Player, bool>), typeof(bool), new[] { typeof(Player) }, out callback)) State.ShouldDrawWorldPlayer = (Func<Player, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "world-item-culling", "ShouldDrawWorldItem", typeof(Func<int, bool>), typeof(bool), new[] { typeof(int) }, out callback)) State.ShouldDrawWorldItem = (Func<int, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "world-particle-culling", "ShouldDrawWorldParticle", typeof(Func<ParticleRenderer, IParticle, bool>), typeof(bool), new[] { typeof(ParticleRenderer), typeof(IParticle) }, out callback)) State.ShouldDrawWorldParticle = (Func<ParticleRenderer, IParticle, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "gore-system", "ShouldRunGoreSystem", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.ShouldRunGoreSystem = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "paint-preparation", "IsPaintPreparationOptimizationEnabled", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.IsPaintPreparationOptimizationEnabled = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "paint-extra-preparation", "IsPaintExtraPreparationRelevant", typeof(Func<int, bool>), typeof(bool), new[] { typeof(int) }, out callback)) State.IsPaintExtraPreparationRelevant = (Func<int, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "clothing-entity-presentation", "IsClothingEntityPresentationOptimizationEnabled", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.IsClothingEntityPresentationOptimizationEnabled = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "clothing-entity-preparation-frame", "BeginClothingEntityPreparationFrame", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) State.BeginClothingEntityPreparationFrame = (Action)callback;
            if (TryResolveOptionalCapability(bridgeType, "clothing-entity-preparation-admission", "TryBeginClothingEntityPreparation", typeof(Func<int, long, bool>), typeof(bool), new[] { typeof(int), typeof(long) }, out callback)) State.TryBeginClothingEntityPreparation = (Func<int, long, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "clothing-entity-preparation-completion", "CompleteClothingEntityPreparation", typeof(Action<int, long>), typeof(void), new[] { typeof(int), typeof(long) }, out callback)) State.CompleteClothingEntityPreparation = (Action<int, long>)callback;
            if (TryResolveOptionalCapability(bridgeType, "waterfall-presentation", "IsWaterfallPresentationOptimizationEnabled", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.IsWaterfallPresentationOptimizationEnabled = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "tile-drawing-presentation", "IsTileDrawingPresentationOptimizationEnabled", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.IsTileDrawingPresentationOptimizationEnabled = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "draw-orchestration", "IsDrawOrchestrationOptimizationEnabled", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.IsDrawOrchestrationOptimizationEnabled = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "paladin-shield-icon", "ShouldDrawPaladinShieldIcon", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.ShouldDrawPaladinShieldIcon = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "laser-ruler-presentation", "TryDrawLaserRulerPresentation", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) State.TryDrawLaserRulerPresentation = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "rain-presentation-begin", "TryBeginRainPresentation", typeof(Func<bool, bool>), typeof(bool), new[] { typeof(bool) }, out callback)) State.TryBeginRainPresentation = (Func<bool, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "rain-presentation-queue", "TryQueueRainPresentation", typeof(PluginUiRuntimeBridgeState.RainPresentationQueueDelegate), typeof(bool), new[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color), typeof(float), typeof(Vector2), typeof(float), typeof(SpriteEffects), typeof(float) }, out callback)) State.TryQueueRainPresentation = (PluginUiRuntimeBridgeState.RainPresentationQueueDelegate)callback;
            if (TryResolveOptionalCapability(bridgeType, "rain-presentation-end", "EndRainPresentation", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) State.EndRainPresentation = (Action)callback;
            State.RainPresentationCapabilitiesResolved = State.TryBeginRainPresentation != null && State.TryQueueRainPresentation != null && State.EndRainPresentation != null;
            if (TryResolveOptionalCapability(bridgeType, "lighting-parallelism", "TryRunLightingParallel", typeof(PluginUiRuntimeBridgeState.LightingParallelDelegate), typeof(bool), new[] { typeof(int), typeof(int), typeof(Delegate), typeof(object) }, out callback)) State.TryRunLightingParallel = (PluginUiRuntimeBridgeState.LightingParallelDelegate)callback;
            if (TryResolveOptionalCapability(bridgeType, "static-tile-chunk-presentation", "TryDrawStaticTileChunk", typeof(PluginUiRuntimeBridgeState.StaticTileChunkDrawDelegate), typeof(bool), new[] { typeof(TileDrawing), typeof(bool), typeof(Vector2), typeof(Vector2), typeof(int), typeof(int) }, out callback)) State.TryDrawStaticTileChunk = (PluginUiRuntimeBridgeState.StaticTileChunkDrawDelegate)callback;
            if (TryResolveOptionalCapability(bridgeType, "static-tile-chunk-invalidation", "InvalidateStaticTileChunks", typeof(Action<int, int>), typeof(void), new[] { typeof(int), typeof(int) }, out callback)) State.InvalidateStaticTileChunks = (Action<int, int>)callback;
            if (TryResolveOptionalCapability(bridgeType, "plugin-commands", "TryHandlePluginChatCommand", typeof(Func<string, bool>), typeof(bool), new[] { typeof(string) }, out callback)) State.TryHandlePluginChatCommand = (Func<string, bool>)callback;
        }

        private static bool EnsureNotificationCapability()
        {
            if (State.NotificationCapabilitiesResolved)
                return State.DrawNotifications != null;
            State.NotificationCapabilitiesResolved = true;
            if (!EnsureBridgeAssembly())
                return false;
            if (!TryResolveOptionalCapability(State.BridgeType, "notifications", "DrawNotifications", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out var callback))
                return false;
            State.DrawNotifications = (Action<SpriteBatch>)callback;
            return true;
        }

        private static bool EnsureRainPresentationCapabilities()
        {
            if (!State.RainPresentationCapabilitiesResolved && !EnsureRuntimeCapabilities())
            {
                return false;
            }

            return State.RainPresentationCapabilitiesResolved &&
                State.TryBeginRainPresentation != null &&
                State.TryQueueRainPresentation != null &&
                State.EndRainPresentation != null;
        }

        private static bool TryResolveOptionalCapability(Type bridgeType, string capability, string methodName, Type delegateType, Type returnType, Type[] parameterTypes, out Delegate callback)
        {
            callback = null;
            string diagnostic;
            MethodInfo method;
            if (!State.Reflection.TryResolveStaticMethod(bridgeType, methodName, returnType, parameterTypes, out method, out diagnostic) ||
                !State.Reflection.TryCreateDelegate(method, delegateType, out callback, out diagnostic))
            {
                SetCapabilityDiagnostic(capability, diagnostic);
                return false;
            }
            SetCapabilityDiagnostic(capability, string.Empty);
            return true;
        }

        private static void SetCapabilityDiagnostic(string capability, string diagnostic)
        {
            lock (State.CapabilityDiagnosticGate)
                State.CapabilityDiagnostics[capability] = diagnostic ?? string.Empty;
        }

        private static bool EnsureChatBridge()
        {
            if (State.ChatBridgeResolved)
                return State.HasChatInputEditors != null;
            State.ChatBridgeResolved = true;
            if (!EnsureBridgeAssembly())
                return false;

            try
            {
                Type bridgeType = State.BridgeType;
                string diagnostic;
                MethodInfo method;
                Delegate callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "HasChatInputEditors", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); return false; }
                State.HasChatInputEditors = (Func<bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "ShouldHandleChatInputAction", typeof(bool), new[] { typeof(string) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.ShouldHandleChatInputAction = (Func<string, bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "ProcessChatInput", typeof(string), new[] { typeof(string), typeof(bool) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string, bool, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.ProcessChatInput = (Func<string, bool, string>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "TryProcessChatActionInput", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.TryProcessChatActionInput = (Func<bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "TryHandleChatActionEscape", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.TryHandleChatActionEscape = (Func<bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "TryApplyChatInputAction", typeof(bool), new[] { typeof(string), typeof(int), typeof(int), typeof(string), typeof(bool), typeof(bool), typeof(int), typeof(string).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(PluginUiRuntimeBridgeState.NativeChatActionDelegate), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.TryApplyChatInputAction = (PluginUiRuntimeBridgeState.NativeChatActionDelegate)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "RecordSubmittedChatInput", typeof(void), new[] { typeof(string) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action<string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.RecordSubmittedChatInput = (Action<string>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "TryDeferOutgoingChatMessage", typeof(bool), new[] { typeof(string) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.TryDeferOutgoingChatMessage = (Func<string, bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "TakeReadyOutgoingChatMessage", typeof(string), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.TakeReadyOutgoingChatMessage = (Func<string>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "HasReadyOutgoingChatMessage", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.HasReadyOutgoingChatMessage = (Func<bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "DrawChatActionStrip", typeof(void), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.DrawChatActionStrip = (Action)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "FormatChatInputForDraw", typeof(string), new[] { typeof(string) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.FormatChatInputForDraw = (Func<string, string>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "DecorateStoredChatMessage", typeof(object), new[] { typeof(object), typeof(Color), typeof(string) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<object, Color, string, object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.DecorateStoredChatMessage = (Func<object, Color, string, object>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "PrepareStoredChatMessageText", typeof(string), new[] { typeof(string), typeof(object) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<string, object, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.PrepareStoredChatMessageText = (Func<string, object, string>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "BeginStoredChatMessageDecoration", typeof(void), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.BeginStoredChatMessageDecoration = (Action)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "BeginStoredChatMessageDecorationForContainer", typeof(void), new[] { typeof(object) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action<object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.BeginStoredChatMessageDecorationForContainer = (Action<object>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "EndStoredChatMessageDecoration", typeof(void), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.EndStoredChatMessageDecoration = (Action)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "RefreshStoredChatMessagePresentations", typeof(void), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.RefreshStoredChatMessagePresentations = (Action)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "ShouldDisplayNetworkChatMessage", typeof(bool), new[] { typeof(byte) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<byte, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.ShouldDisplayNetworkChatMessage = (Func<byte, bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "ShouldDisplayLocalChatMessage", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.ShouldDisplayLocalChatMessage = (Func<bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "HandleChatSnippetHover", typeof(void), new[] { typeof(object) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action<object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.HandleChatSnippetHover = (Action<object>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "HandleChatSnippetClick", typeof(bool), new[] { typeof(object) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<object, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.HandleChatSnippetClick = (Func<object, bool>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "GetChatSnippetVisibleColor", typeof(Color), new[] { typeof(object), typeof(Color) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Func<object, Color, Color>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.GetChatSnippetVisibleColor = (Func<object, Color, Color>)callback;
                if (!State.Reflection.TryResolveStaticMethod(bridgeType, "CopyChatSnippetContext", typeof(void), new[] { typeof(object), typeof(object) }, out method, out diagnostic) || !State.Reflection.TryCreateDelegate(method, typeof(Action<object, object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                State.CopyChatSnippetContext = (Action<object, object>)callback;
                return true;
            }
            catch (Exception exception)
            {
                RecordFailure("Resolve chat extension bridge", exception);
                ClearChatDelegates();
                return false;
            }
        }

        private static void ClearBridgeDelegates()
        {
            ClearPluginManagerDelegates();
            State.DrawHudWidgets = null;
            State.DrawWorldOverlays = null;
            State.DrawMenuOverlays = null;
            State.CaptureMeleeCollisionBounds = null;
            State.UpdatePluginKeybinds = null;
            State.EnsurePluginKeybindStateShape = null;
            State.AppendPluginKeybindControls = null;
            State.ShouldRunDustSystem = null;
            State.ShouldCreateDust = null;
            State.ShouldUpdateDustInstance = null;
            State.ShouldDrawDustInstance = null;
            State.ShouldDrawWorldPlayer = null;
            State.ShouldDrawWorldItem = null;
            State.ShouldDrawWorldParticle = null;
            State.ShouldRunGoreSystem = null;
            State.IsPaintPreparationOptimizationEnabled = null;
            State.IsPaintExtraPreparationRelevant = null;
            State.IsClothingEntityPresentationOptimizationEnabled = null;
            State.BeginClothingEntityPreparationFrame = null;
            State.TryBeginClothingEntityPreparation = null;
            State.CompleteClothingEntityPreparation = null;
            State.IsWaterfallPresentationOptimizationEnabled = null;
            State.IsTileDrawingPresentationOptimizationEnabled = null;
            State.IsDrawOrchestrationOptimizationEnabled = null;
            State.ShouldDrawPaladinShieldIcon = null;
            State.TryDrawLaserRulerPresentation = null;
            State.TryBeginRainPresentation = null;
            State.TryQueueRainPresentation = null;
            State.EndRainPresentation = null;
            State.RainPresentationCapabilitiesResolved = false;
            State.TryRunLightingParallel = null;
            State.TryDrawStaticTileChunk = null;
            State.InvalidateStaticTileChunks = null;
            State.TryHandlePluginChatCommand = null;
            State.BootstrapPluginRuntime = null;
            State.ShutdownPluginRuntime = null;
            State.RuntimeCapabilitiesResolved = false;
            State.NotificationCapabilitiesResolved = false;
        }

        private static void ClearPluginManagerDelegates()
        {
            State.Open = null;
            State.OpenIngamePluginSettings = null;
            State.DrawIngamePluginSettings = null;
            State.DrawNotifications = null;
            State.HandlePluginMenuInput = null;
        }

        private static void ClearChatDelegates()
        {
            State.HasChatInputEditors = null;
            State.ShouldHandleChatInputAction = null;
            State.ProcessChatInput = null;
            State.TryProcessChatActionInput = null;
            State.TryHandleChatActionEscape = null;
            State.TryApplyChatInputAction = null;
            State.RecordSubmittedChatInput = null;
            State.TryDeferOutgoingChatMessage = null;
            State.TakeReadyOutgoingChatMessage = null;
            State.HasReadyOutgoingChatMessage = null;
            State.DrawChatActionStrip = null;
            State.FormatChatInputForDraw = null;
            State.DecorateStoredChatMessage = null;
            State.PrepareStoredChatMessageText = null;
            State.BeginStoredChatMessageDecoration = null;
            State.BeginStoredChatMessageDecorationForContainer = null;
            State.EndStoredChatMessageDecoration = null;
            State.RefreshStoredChatMessagePresentations = null;
            State.ShouldDisplayNetworkChatMessage = null;
            State.ShouldDisplayLocalChatMessage = null;
            State.HandleChatSnippetHover = null;
            State.HandleChatSnippetClick = null;
            State.GetChatSnippetVisibleColor = null;
            State.CopyChatSnippetContext = null;
        }

        private static bool TryProcessChatActionInput()
        {
            return EnsureChatBridge() && State.TryProcessChatActionInput != null && State.TryProcessChatActionInput();
        }

        private static bool TryHandleChatActionEscape()
        {
            return EnsureChatBridge() && State.TryHandleChatActionEscape != null && State.TryHandleChatActionEscape();
        }

        private static bool TryApplyChatInputAction(
            string text,
            int caret,
            int selectionAnchor,
            string actionId,
            bool control,
            bool shift,
            int scrollLines,
            out string resultText,
            out int resultCaret,
            out int resultSelectionAnchor,
            out int appliedScrollLines)
        {
            resultText = text ?? string.Empty;
            resultCaret = caret;
            resultSelectionAnchor = selectionAnchor;
            appliedScrollLines = 0;
            return EnsureChatBridge() && State.TryApplyChatInputAction != null && State.TryApplyChatInputAction(
                resultText,
                caret,
                selectionAnchor,
                actionId,
                control,
                shift,
                scrollLines,
                out resultText,
                out resultCaret,
                out resultSelectionAnchor,
                out appliedScrollLines);
        }

        private static bool HandlePluginMenuInput()
        {
            return EnsureBridge() && State.HandlePluginMenuInput != null ? State.HandlePluginMenuInput() : true;
        }

        private static bool IsIngamePluginsCategory()
        {
            FieldInfo category;
            return TryGetIngameOptionsCategoryField(out category) && (int)category.GetValue(null) == IngamePluginsCategory;
        }

        private static void SetIngamePluginsCategory()
        {
            FieldInfo category;
            if (TryGetIngameOptionsCategoryField(out category))
                category.SetValue(null, IngamePluginsCategory);
        }

        private static void RestoreIngameOptionsCategory()
        {
            FieldInfo category;
            if (TryGetIngameOptionsCategoryField(out category))
                category.SetValue(null, 0);
        }

        private static bool TryGetIngameOptionsCategoryField(out FieldInfo field)
        {
            string diagnostic;
            bool available = State.Reflection.TryResolveStaticField(typeof(IngameOptions), "category", typeof(int), out field, out diagnostic);
            if (!available) RecordUnavailable(diagnostic);
            return available;
        }

        private static bool TryGetMenuModeField(out FieldInfo field)
        {
            string diagnostic;
            bool available = State.Reflection.TryResolveStaticField(typeof(Main), "menuMode", typeof(int), out field, out diagnostic);
            if (!available) RecordUnavailable(diagnostic);
            return available;
        }

        private static int ReadMenuMode(FieldInfo field) { return (int)field.GetValue(null); }
        private static void SetMenuMode(FieldInfo field, int value) { field.SetValue(null, value); }

        private static void RecordUnavailable(string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic))
                diagnostic = "Unavailable: a required Alacrity UI bridge member could not be resolved.";
            RecordDiagnostic(diagnostic);
        }

        private static void RecordFailure(string operation, Exception exception)
        {
            RecordDiagnostic("Failed: " + operation + ": " + exception.GetType().Name + ": " + exception.Message, exception);
        }

        private static void RecordDiagnostic(string diagnostic, Exception exception = null)
        {
            if (string.Equals(State.LastDiagnostic, diagnostic, StringComparison.Ordinal))
                return;

            State.LastDiagnostic = diagnostic;
            try
            {
                string detail = exception == null ? diagnostic : diagnostic + Environment.NewLine + exception;
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alacrity-plugin-ui-error.log"), detail);
            }
            catch (Exception writeFailure)
            {
                Debug.WriteLine("Alacrity UI diagnostic logging failed: " + writeFailure.Message);
            }
        }
    }
}
