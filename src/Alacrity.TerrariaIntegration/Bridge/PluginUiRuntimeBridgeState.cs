using System;
using System.Collections.Generic;
using System.Reflection;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.States;
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
        internal readonly BridgeCompatibilityDescriptor ExpectedBridgeCompatibility = new BridgeCompatibilityDescriptor(3, 2, 2, "1.4.5.6");
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
        internal Func<string, bool> TryHandlePluginChatCommand;
        internal Func<bool> HandlePluginMenuInput;
        internal Func<bool> HasChatInputEditors;
        internal Func<string, bool> ShouldHandleChatInputAction;
        internal Func<string, bool, string> ProcessChatInput;
        internal Action<string> RecordSubmittedChatInput;
        internal Func<string, string> FormatChatInputForDraw;
        internal Func<object, Color, string, object> DecorateChatMessage;
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
        internal string LastDiagnostic;
        internal bool ShutdownHooked;
    }
}
