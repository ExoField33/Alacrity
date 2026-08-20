using System;
using System.Collections.Generic;
using System.Reflection;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameContent.Drawing;
using Terraria.Graphics.Renderers;

namespace AlacrityTerraria
{
    /// <summary>
    /// Mutable delegate and diagnostic cache owned by the version-locked bridge facade. Keeping
    /// it separate prevents the patch-facing type from becoming the runtime's state container.
    /// All fields are resolved once and then read directly from hot forwarding paths.
    /// </summary>
    internal sealed class PluginUiRuntimeBridgeState
    {
        // This is compiled into the facade so a stale PluginSdk cannot make the compatibility
        // diagnostic itself uncallable. Keep it synchronized with the Core bridge handshake.
        internal readonly BridgeCompatibilityDescriptor ExpectedBridgeCompatibility = new BridgeCompatibilityDescriptor(5, 2, 14, "1.4.5.6");
        internal readonly BridgeReflectionResolver Reflection = new BridgeReflectionResolver();
        internal readonly object CapabilityDiagnosticGate = new object();
        internal readonly Dictionary<string, string> CapabilityDiagnostics = new Dictionary<string, string>(StringComparer.Ordinal);

        internal FieldInfo VersionNumber;
        internal Assembly BridgeAssembly;
        internal Type BridgeType;
        internal Action BootstrapPluginRuntime;
        internal Func<string> GetBridgeHandshake;
        internal Action ShutdownPluginRuntime;
        internal Action Open;
        internal Action OpenIngamePluginSettings;
        internal Action<SpriteBatch> DrawIngamePluginSettings;
        internal Action<SpriteBatch> DrawNotifications;
        internal Action<SpriteBatch> DrawHudWidgets;
        internal Action<SpriteBatch> DrawWorldOverlays;
        internal Action<SpriteBatch> DrawMenuOverlays;
        internal Action<Player, bool, Rectangle> CaptureMeleeCollisionBounds;
        internal Action UpdatePluginKeybinds;
        internal Action EnsurePluginKeybindStateShape;
        internal Action<UIManageControls> AppendPluginKeybindControls;
        internal Func<bool> ShouldRunDustSystem;
        internal Func<int, bool> ShouldCreateDust;
        internal Func<Dust, bool> ShouldUpdateDustInstance;
        internal Func<Dust, bool> ShouldDrawDustInstance;
        internal Func<Player, bool> ShouldDrawWorldPlayer;
        internal Func<int, bool> ShouldDrawWorldItem;
        internal Func<ParticleRenderer, IParticle, bool> ShouldDrawWorldParticle;
        internal Func<bool> ShouldRunGoreSystem;
        internal Func<bool> IsPaintPreparationOptimizationEnabled;
        internal Func<int, bool> IsPaintExtraPreparationRelevant;
        internal Func<bool> IsClothingEntityPresentationOptimizationEnabled;
        internal Func<bool> IsWaterfallPresentationOptimizationEnabled;
        internal Func<bool> IsTileDrawingPresentationOptimizationEnabled;
        internal Func<bool> IsDrawOrchestrationOptimizationEnabled;
        internal Func<bool> ShouldDrawPaladinShieldIcon;
        internal Func<bool> TryDrawLaserRulerPresentation;
        internal Func<bool, bool> TryBeginRainPresentation;
        internal RainPresentationQueueDelegate TryQueueRainPresentation;
        internal Action EndRainPresentation;
        internal bool RainPresentationCapabilitiesResolved;
        internal LightingParallelDelegate TryRunLightingParallel;
        internal StaticTileChunkDrawDelegate TryDrawStaticTileChunk;
        internal Action<int, int> InvalidateStaticTileChunks;
        internal Func<string, bool> TryHandlePluginChatCommand;
        internal Func<bool> HandlePluginMenuInput;
        internal Func<bool> HasChatInputEditors;
        internal Func<string, bool> ShouldHandleChatInputAction;
        internal Func<string, bool, string> ProcessChatInput;
        internal Func<bool> TryProcessChatActionInput;
        internal Func<bool> TryHandleChatActionEscape;
        internal NativeChatActionDelegate TryApplyChatInputAction;
        internal Action<string> RecordSubmittedChatInput;
        internal Func<string, bool> TryDeferOutgoingChatMessage;
        internal Func<string> TakeReadyOutgoingChatMessage;
        internal Func<bool> HasReadyOutgoingChatMessage;
        internal Action DrawChatActionStrip;
        internal Func<string, string> FormatChatInputForDraw;
        internal Func<object, Color, string, object> DecorateStoredChatMessage;
        internal Func<string, object, string> PrepareStoredChatMessageText;
        internal Action BeginStoredChatMessageDecoration;
        internal Action<object> BeginStoredChatMessageDecorationForContainer;
        internal Action EndStoredChatMessageDecoration;
        internal Action RefreshStoredChatMessagePresentations;
        internal Func<byte, bool> ShouldDisplayNetworkChatMessage;
        internal Func<bool> ShouldDisplayLocalChatMessage;
        internal Action<object> HandleChatSnippetHover;
        internal Func<object, bool> HandleChatSnippetClick;
        internal Func<object, Color, Color> GetChatSnippetVisibleColor;
        internal Action<object, object> CopyChatSnippetContext;
        internal bool ChatBridgeResolved;
        internal Action<Color, float> DrawVersionNumber;
        internal bool VersionRendererResolved;
        internal bool BridgeLoadAttempted;
        internal bool RuntimeCapabilitiesResolved;
        internal bool PluginManagerCapabilitiesResolved;
        internal bool NotificationCapabilitiesResolved;
        // Set during the post-input pass and consumed at Player.ToggleInv's verified native
        // IngameOptions.Close call. This keeps one Escape inside an Alacrity chooser from
        // also closing Terraria's entire in-game options window.
        internal bool SuppressNextIngameOptionsClose;
        internal string LastDiagnostic;
        internal bool ShutdownHooked;

        internal delegate bool StaticTileChunkDrawDelegate(
            TileDrawing drawing,
            bool solidLayer,
            Vector2 screenPosition,
            Vector2 screenOffset,
            int tileX,
            int tileY);

        internal delegate bool RainPresentationQueueDelegate(
            Texture2D texture,
            Vector2 position,
            Rectangle? source,
            Color color,
            float rotation,
            Vector2 origin,
            float scale,
            SpriteEffects effects,
            float layerDepth);

        internal delegate bool LightingParallelDelegate(
            int fromInclusive,
            int toExclusive,
            Delegate callback,
            object context);

        internal delegate bool NativeChatActionDelegate(
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
            out int appliedScrollLines);
    }
}
