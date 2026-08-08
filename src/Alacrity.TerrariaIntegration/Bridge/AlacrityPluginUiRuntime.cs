using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria;
using Terraria.GameContent.UI.States;

namespace AlacrityTerraria
{
    // The injected entry point stays independent from SDK/Core until the optional Plugins UI is opened.
    // Every reflected member is exact-signature checked and cached so unavailable bridge code falls back to Terraria.
    public static class PluginUiRuntime
    {
        private const int PluginMenuMode = 888;
        private const int IngamePluginsCategory = 777016;
        private static readonly BridgeReflectionResolver Reflection = new BridgeReflectionResolver();
        private static FieldInfo _versionNumber;
        private static Assembly _bridgeAssembly;
        private static Type _bridgeType;
        private static Action _bootstrapPluginRuntime;
        private static Func<string> _getBridgeHandshake;
        private static Action _shutdownPluginRuntime;
        private static Action _open;
        private static Action _openIngamePluginSettings;
        private static Action<SpriteBatch> _drawIngamePluginSettings;
        private static Action<SpriteBatch> _drawNotifications;
        private static Action<SpriteBatch> _drawHudWidgets;
        private static Action<SpriteBatch> _drawWorldOverlays;
        private static Action<SpriteBatch> _drawMenuOverlays;
        private static Action<Player, bool, Rectangle> _captureMeleeCollisionBounds;
        private static Action _updatePluginKeybinds;
        private static Action _ensurePluginKeybindStateShape;
        private static Action<UIManageControls> _appendPluginKeybindControls;
        private static Func<bool> _shouldRunDustSystem;
        private static Func<int, bool> _shouldCreateDust;
        private static Func<Dust, bool> _shouldUpdateDustInstance;
        private static Func<Dust, bool> _shouldDrawDustInstance;
        private static Func<bool> _shouldRunGoreSystem;
        private static Func<string, bool> _tryHandlePluginChatCommand;
        private static Func<bool> _handlePluginMenuInput;
        private static Func<bool> _hasChatInputEditors;
        private static Func<string, bool, string> _processChatInput;
        private static Func<string, string> _formatChatInputForDraw;
        private static Func<object, Color, string, object> _decorateChatMessage;
        private static Func<byte, bool> _shouldDisplayNetworkChatMessage;
        private static Func<bool> _shouldDisplayLocalChatMessage;
        private static Action<object> _handleChatSnippetHover;
        private static Func<object, bool> _handleChatSnippetClick;
        private static Func<object, Color, Color> _getChatSnippetVisibleColor;
        private static Action<object, object> _copyChatSnippetContext;
        private static bool _chatBridgeResolved;
        private static Action<Color, float> _drawVersionNumber;
        private static bool _versionRendererResolved;
        private static bool _bridgeLoadAttempted;
        private static bool _runtimeCapabilitiesResolved;
        private static bool _pluginManagerCapabilitiesResolved;
        private static bool _notificationCapabilitiesResolved;
        private static readonly object CapabilityDiagnosticGate = new object();
        private static readonly Dictionary<string, string> CapabilityDiagnostics = new Dictionary<string, string>(StringComparer.Ordinal);
        private static string _lastDiagnostic;
        private static bool _shutdownHooked;

        /// <summary>Latest bridge availability or failure diagnostic for support and crash reports.</summary>
        public static string LastBridgeDiagnostic { get { return _lastDiagnostic ?? string.Empty; } }

        /// <summary>Returns the cached diagnostic for one independently resolved bridge capability.</summary>
        public static string GetBridgeCapabilityDiagnostic(string capability)
        {
            if (string.IsNullOrWhiteSpace(capability))
                throw new ArgumentException("A bridge capability name is required.", nameof(capability));
            lock (CapabilityDiagnosticGate)
                return CapabilityDiagnostics.TryGetValue(capability, out var diagnostic) ? diagnostic : string.Empty;
        }

        public static bool HandleInput()
        {
            try
            {
                FieldInfo menuMode;
                if (!TryGetMenuModeField(out menuMode))
                    return true;

                return ReadMenuMode(menuMode) == PluginMenuMode ? HandlePluginMenuInput() : true;
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

        /// <summary>Version-locked startup entry point. It is safe to call more than once.</summary>
        public static void BootstrapPluginRuntime()
        {
            try
            {
                if (!EnsureRuntimeCapabilities()) return;
                if (!_shutdownHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => ShutdownPluginRuntime();
                    _shutdownHooked = true;
                }
                _bootstrapPluginRuntime?.Invoke();
            }
            catch (Exception exception) { RecordFailure("Plugin runtime startup", exception); }
        }

        private static void ShutdownPluginRuntime()
        {
            try
            {
                _shutdownPluginRuntime?.Invoke();
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
                _open();
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
                string originalVersion = (string)_versionNumber.GetValue(null);
                try
                {
                    _versionNumber.SetValue(null, versionText);
                    _drawVersionNumber(color, verticalOffset);
                    if (_runtimeCapabilitiesResolved)
                        _drawMenuOverlays?.Invoke(Main.spriteBatch);
                }
                finally
                {
                    _versionNumber.SetValue(null, originalVersion);
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
                if (!EnsureBridge() || _openIngamePluginSettings == null)
                {
                    RestoreIngameOptionsCategory();
                    return;
                }

                _openIngamePluginSettings();
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
                if (!EnsureBridge() || _drawIngamePluginSettings == null)
                {
                    RestoreIngameOptionsCategory();
                    return;
                }

                _drawIngamePluginSettings(spriteBatch);
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
                        _drawNotifications?.Invoke(spriteBatch);
                        _drawHudWidgets?.Invoke(spriteBatch);
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
                    _updatePluginKeybinds?.Invoke();
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
                if (!EnsureRuntimeCapabilities() || _drawWorldOverlays == null)
                    return;
                _drawWorldOverlays(spriteBatch);
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
                Action<Player, bool, Rectangle> capture = _captureMeleeCollisionBounds;
                if (capture != null)
                    capture(player, dontAttack, hitbox);
                else if (EnsureRuntimeCapabilities())
                    _captureMeleeCollisionBounds?.Invoke(player, dontAttack, hitbox);
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
                    _ensurePluginKeybindStateShape?.Invoke();
            }
            catch (Exception exception)
            {
                RecordFailure("Synchronize plugin keybind state", exception);
            }
        }

        // These version-locked calls fail open: an unavailable plugin bridge must never suppress vanilla effects.
        public static bool ShouldRunDustSystem() => EnsureRuntimeCapabilities() && _shouldRunDustSystem != null ? _shouldRunDustSystem() : true;
        public static bool ShouldCreateDust(int dustType) => EnsureRuntimeCapabilities() && _shouldCreateDust != null ? _shouldCreateDust(dustType) : true;
        public static bool ShouldUpdateDustInstance(Dust dust) => EnsureRuntimeCapabilities() && _shouldUpdateDustInstance != null ? _shouldUpdateDustInstance(dust) : true;
        public static bool ShouldDrawDustInstance(Dust dust) => EnsureRuntimeCapabilities() && _shouldDrawDustInstance != null ? _shouldDrawDustInstance(dust) : true;
        public static bool ShouldRunGoreSystem() => EnsureRuntimeCapabilities() && _shouldRunGoreSystem != null ? _shouldRunGoreSystem() : true;

        public static bool TryHandlePluginChatCommand(string text)
        {
            return EnsureRuntimeCapabilities() && _tryHandlePluginChatCommand != null && _tryHandlePluginChatCommand(text);
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
                    _appendPluginKeybindControls?.Invoke(controls);
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
            return EnsureChatBridge() && _hasChatInputEditors != null && _hasChatInputEditors();
        }

        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            return ProcessChatInput(text, allowMultiLine);
        }

        public static string ProcessChatInput(string text, bool allowMultiLine)
        {
            return EnsureChatBridge() && _processChatInput != null ? _processChatInput(text, allowMultiLine) : text;
        }

        public static string FormatPlayerChatText(string text)
        {
            return FormatChatInputForDraw(text);
        }

        public static string FormatChatInputForDraw(string text)
        {
            if (EnsureChatBridge() && _formatChatInputForDraw != null)
                return _formatChatInputForDraw(text);
            return Main.instance != null && Main.instance.textBlinkerState == 1 ? (text ?? string.Empty) + "|" : text;
        }

        public static object DecorateChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            return EnsureChatBridge() && _decorateChatMessage != null ? _decorateChatMessage(snippets, baseColor, originalMessage) : snippets;
        }

        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            return EnsureChatBridge() && _shouldDisplayNetworkChatMessage != null ? _shouldDisplayNetworkChatMessage(messageAuthor) : true;
        }

        public static bool ShouldDisplayLocalChatMessage()
        {
            return EnsureChatBridge() && _shouldDisplayLocalChatMessage != null ? _shouldDisplayLocalChatMessage() : true;
        }

        public static void HandleChatSnippetHover(object snippet)
        {
            if (EnsureChatBridge() && _handleChatSnippetHover != null)
                _handleChatSnippetHover(snippet);
        }

        public static bool HandleChatSnippetClick(object snippet)
        {
            return EnsureChatBridge() && _handleChatSnippetClick != null && _handleChatSnippetClick(snippet);
        }

        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            return EnsureChatBridge() && _getChatSnippetVisibleColor != null ? _getChatSnippetVisibleColor(snippet, color) : color;
        }

        public static void CopyChatSnippetContext(object source, object copy)
        {
            if (EnsureChatBridge() && _copyChatSnippetContext != null)
                _copyChatSnippetContext(source, copy);
        }

        private static bool EnsureVersionRenderer()
        {
            if (_versionRendererResolved)
                return _drawVersionNumber != null && _versionNumber != null;

            _versionRendererResolved = true;
            string diagnostic;
            MethodInfo renderer;
            if (!Reflection.TryResolveStaticField(typeof(Main), "versionNumber", typeof(string), out _versionNumber, out diagnostic) ||
                !Reflection.TryResolveStaticMethod(typeof(Main), "DrawVersionNumber", typeof(void), new[] { typeof(Color), typeof(float) }, out renderer, out diagnostic))
            {
                RecordUnavailable(diagnostic);
                return false;
            }

            Delegate callback;
            if (!Reflection.TryCreateDelegate(renderer, typeof(Action<Color, float>), out callback, out diagnostic))
            {
                RecordUnavailable(diagnostic);
                return false;
            }

            _drawVersionNumber = (Action<Color, float>)callback;
            return true;
        }

        private static bool EnsureBridge()
        {
            if (_pluginManagerCapabilitiesResolved)
                return _open != null;

            if (!EnsureBridgeAssembly())
                return false;

            _pluginManagerCapabilitiesResolved = true;
            try
            {
                Type bridgeType = _bridgeType;
                string diagnostic;
                MethodInfo open;
                MethodInfo openIngame;
                MethodInfo drawIngame;
                MethodInfo handleInput;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "Open", typeof(void), Type.EmptyTypes, out open, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "OpenIngamePluginSettings", typeof(void), Type.EmptyTypes, out openIngame, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "DrawIngamePluginSettings", typeof(void), new[] { typeof(SpriteBatch) }, out drawIngame, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "HandlePluginMenuInput", typeof(bool), Type.EmptyTypes, out handleInput, out diagnostic))
                {
                    SetCapabilityDiagnostic("plugin-manager", diagnostic);
                    RecordUnavailable(diagnostic);
                    ClearPluginManagerDelegates();
                    return false;
                }

                Delegate callback = null;
                if (!Reflection.TryCreateDelegate(open, typeof(Action), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                _open = (Action)callback;
                if (!Reflection.TryCreateDelegate(openIngame, typeof(Action), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                _openIngamePluginSettings = (Action)callback;
                if (!Reflection.TryCreateDelegate(drawIngame, typeof(Action<SpriteBatch>), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                _drawIngamePluginSettings = (Action<SpriteBatch>)callback;
                if (!Reflection.TryCreateDelegate(handleInput, typeof(Func<bool>), out callback, out diagnostic)) { SetCapabilityDiagnostic("plugin-manager", diagnostic); RecordUnavailable(diagnostic); ClearPluginManagerDelegates(); return false; }
                _handlePluginMenuInput = (Func<bool>)callback;
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
            if (_bridgeType != null)
                return true;
            if (_bridgeLoadAttempted)
                return false;

            _bridgeLoadAttempted = true;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "Alacrity.PluginUiCoreBridge.dll");
            if (!File.Exists(path))
            {
                RecordUnavailable("Unavailable: Alacrity.PluginUiCoreBridge.dll was not found at " + path + ".");
                return false;
            }

            try
            {
                _bridgeAssembly = Assembly.LoadFrom(path);
                _bridgeType = _bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", false);
                if (_bridgeType == null)
                {
                    RecordUnavailable("Unavailable: the UI bridge does not contain AlacrityTerraria.PluginUiRuntime.");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                _bridgeAssembly = null;
                _bridgeType = null;
                RecordFailure("Load plugin bridge assembly", exception);
                return false;
            }
        }

        private static bool EnsureRuntimeCapabilities()
        {
            if (_runtimeCapabilitiesResolved)
                return _bootstrapPluginRuntime != null;
            if (!EnsureBridgeAssembly())
                return false;

            _runtimeCapabilitiesResolved = true;
            try
            {
                string diagnostic;
                Delegate callback = null;
                MethodInfo bootstrap;
                MethodInfo shutdown;
                MethodInfo handshake;
                if (!Reflection.TryResolveStaticMethod(_bridgeType, "GetBridgeHandshake", typeof(string), Type.EmptyTypes, out handshake, out diagnostic) ||
                    !Reflection.TryCreateDelegate(handshake, typeof(Func<string>), out callback, out diagnostic))
                {
                    RecordUnavailable("Bridge compatibility handshake is unavailable: " + diagnostic);
                    return false;
                }
                _getBridgeHandshake = (Func<string>)callback;
                if (!string.Equals(_getBridgeHandshake(), "2|2|2|1.4.5.6", StringComparison.Ordinal))
                {
                    RecordUnavailable("Bridge compatibility mismatch. Expected SDK|Core|ABI|Terraria = 2|2|2|1.4.5.6; rebuild/copy Alacrity assemblies together.");
                    return false;
                }
                if (!Reflection.TryResolveStaticMethod(_bridgeType, "BootstrapPluginRuntime", typeof(void), Type.EmptyTypes, out bootstrap, out diagnostic) ||
                    !Reflection.TryCreateDelegate(bootstrap, typeof(Action), out callback, out diagnostic))
                {
                    RecordUnavailable(diagnostic);
                    return false;
                }
                _bootstrapPluginRuntime = (Action)callback;
                if (Reflection.TryResolveStaticMethod(_bridgeType, "ShutdownPluginRuntime", typeof(void), Type.EmptyTypes, out shutdown, out _) &&
                    Reflection.TryCreateDelegate(shutdown, typeof(Action), out callback, out _))
                    _shutdownPluginRuntime = (Action)callback;
                ResolveOptionalCapabilities(_bridgeType);
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
            if (TryResolveOptionalCapability(bridgeType, "hud-widgets", "DrawHudWidgets", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out var callback)) _drawHudWidgets = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "world-overlays", "DrawWorldOverlays", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out callback)) _drawWorldOverlays = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "menu-overlays", "DrawMenuOverlays", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out callback)) _drawMenuOverlays = (Action<SpriteBatch>)callback;
            if (TryResolveOptionalCapability(bridgeType, "combat-collision-capture", "CaptureMeleeCollisionBounds", typeof(Action<Player, bool, Rectangle>), typeof(void), new[] { typeof(Player), typeof(bool), typeof(Rectangle) }, out callback)) _captureMeleeCollisionBounds = (Action<Player, bool, Rectangle>)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-update", "UpdatePluginKeybinds", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) _updatePluginKeybinds = (Action)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-state", "EnsurePluginKeybindStateShape", typeof(Action), typeof(void), Type.EmptyTypes, out callback)) _ensurePluginKeybindStateShape = (Action)callback;
            if (TryResolveOptionalCapability(bridgeType, "keybind-controls", "AppendPluginKeybindControls", typeof(Action<UIManageControls>), typeof(void), new[] { typeof(UIManageControls) }, out callback)) _appendPluginKeybindControls = (Action<UIManageControls>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-system", "ShouldRunDustSystem", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) _shouldRunDustSystem = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-create", "ShouldCreateDust", typeof(Func<int, bool>), typeof(bool), new[] { typeof(int) }, out callback)) _shouldCreateDust = (Func<int, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-update", "ShouldUpdateDustInstance", typeof(Func<Dust, bool>), typeof(bool), new[] { typeof(Dust) }, out callback)) _shouldUpdateDustInstance = (Func<Dust, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "dust-draw", "ShouldDrawDustInstance", typeof(Func<Dust, bool>), typeof(bool), new[] { typeof(Dust) }, out callback)) _shouldDrawDustInstance = (Func<Dust, bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "gore-system", "ShouldRunGoreSystem", typeof(Func<bool>), typeof(bool), Type.EmptyTypes, out callback)) _shouldRunGoreSystem = (Func<bool>)callback;
            if (TryResolveOptionalCapability(bridgeType, "plugin-commands", "TryHandlePluginChatCommand", typeof(Func<string, bool>), typeof(bool), new[] { typeof(string) }, out callback)) _tryHandlePluginChatCommand = (Func<string, bool>)callback;
        }

        private static bool EnsureNotificationCapability()
        {
            if (_notificationCapabilitiesResolved)
                return _drawNotifications != null;
            _notificationCapabilitiesResolved = true;
            if (!EnsureBridgeAssembly())
                return false;
            if (!TryResolveOptionalCapability(_bridgeType, "notifications", "DrawNotifications", typeof(Action<SpriteBatch>), typeof(void), new[] { typeof(SpriteBatch) }, out var callback))
                return false;
            _drawNotifications = (Action<SpriteBatch>)callback;
            return true;
        }

        private static bool TryResolveOptionalCapability(Type bridgeType, string capability, string methodName, Type delegateType, Type returnType, Type[] parameterTypes, out Delegate callback)
        {
            callback = null;
            string diagnostic;
            MethodInfo method;
            if (!Reflection.TryResolveStaticMethod(bridgeType, methodName, returnType, parameterTypes, out method, out diagnostic) ||
                !Reflection.TryCreateDelegate(method, delegateType, out callback, out diagnostic))
            {
                SetCapabilityDiagnostic(capability, diagnostic);
                return false;
            }
            SetCapabilityDiagnostic(capability, string.Empty);
            return true;
        }

        private static void SetCapabilityDiagnostic(string capability, string diagnostic)
        {
            lock (CapabilityDiagnosticGate)
                CapabilityDiagnostics[capability] = diagnostic ?? string.Empty;
        }

        private static bool EnsureChatBridge()
        {
            if (_chatBridgeResolved)
                return _hasChatInputEditors != null;
            _chatBridgeResolved = true;
            if (!EnsureBridgeAssembly())
                return false;

            try
            {
                Type bridgeType = _bridgeType;
                string diagnostic;
                MethodInfo method;
                Delegate callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "HasChatInputEditors", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); return false; }
                _hasChatInputEditors = (Func<bool>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "ProcessChatInput", typeof(string), new[] { typeof(string), typeof(bool) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<string, bool, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _processChatInput = (Func<string, bool, string>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "FormatChatInputForDraw", typeof(string), new[] { typeof(string) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<string, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _formatChatInputForDraw = (Func<string, string>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "DecorateChatMessage", typeof(object), new[] { typeof(object), typeof(Color), typeof(string) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<object, Color, string, object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _decorateChatMessage = (Func<object, Color, string, object>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "ShouldDisplayNetworkChatMessage", typeof(bool), new[] { typeof(byte) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<byte, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _shouldDisplayNetworkChatMessage = (Func<byte, bool>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "ShouldDisplayLocalChatMessage", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _shouldDisplayLocalChatMessage = (Func<bool>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "HandleChatSnippetHover", typeof(void), new[] { typeof(object) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Action<object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _handleChatSnippetHover = (Action<object>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "HandleChatSnippetClick", typeof(bool), new[] { typeof(object) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<object, bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _handleChatSnippetClick = (Func<object, bool>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "GetChatSnippetVisibleColor", typeof(Color), new[] { typeof(object), typeof(Color) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<object, Color, Color>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _getChatSnippetVisibleColor = (Func<object, Color, Color>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "CopyChatSnippetContext", typeof(void), new[] { typeof(object), typeof(object) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Action<object, object>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _copyChatSnippetContext = (Action<object, object>)callback;
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
            _drawHudWidgets = null;
            _drawWorldOverlays = null;
            _drawMenuOverlays = null;
            _captureMeleeCollisionBounds = null;
            _updatePluginKeybinds = null;
            _ensurePluginKeybindStateShape = null;
            _appendPluginKeybindControls = null;
            _shouldRunDustSystem = null;
            _shouldCreateDust = null;
            _shouldUpdateDustInstance = null;
            _shouldDrawDustInstance = null;
            _shouldRunGoreSystem = null;
            _tryHandlePluginChatCommand = null;
            _bootstrapPluginRuntime = null;
            _shutdownPluginRuntime = null;
            _runtimeCapabilitiesResolved = false;
            _notificationCapabilitiesResolved = false;
        }

        private static void ClearPluginManagerDelegates()
        {
            _open = null;
            _openIngamePluginSettings = null;
            _drawIngamePluginSettings = null;
            _drawNotifications = null;
            _handlePluginMenuInput = null;
        }

        private static void ClearChatDelegates()
        {
            _hasChatInputEditors = null;
            _processChatInput = null;
            _formatChatInputForDraw = null;
            _decorateChatMessage = null;
            _shouldDisplayNetworkChatMessage = null;
            _shouldDisplayLocalChatMessage = null;
            _handleChatSnippetHover = null;
            _handleChatSnippetClick = null;
            _getChatSnippetVisibleColor = null;
            _copyChatSnippetContext = null;
        }

        private static bool HandlePluginMenuInput()
        {
            return EnsureBridge() && _handlePluginMenuInput != null ? _handlePluginMenuInput() : true;
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
            bool available = Reflection.TryResolveStaticField(typeof(IngameOptions), "category", typeof(int), out field, out diagnostic);
            if (!available) RecordUnavailable(diagnostic);
            return available;
        }

        private static bool TryGetMenuModeField(out FieldInfo field)
        {
            string diagnostic;
            bool available = Reflection.TryResolveStaticField(typeof(Main), "menuMode", typeof(int), out field, out diagnostic);
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
            if (string.Equals(_lastDiagnostic, diagnostic, StringComparison.Ordinal))
                return;

            _lastDiagnostic = diagnostic;
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
