using System;
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
        private static Action _open;
        private static Action _openIngamePluginSettings;
        private static Action<SpriteBatch> _drawIngamePluginSettings;
        private static Action<SpriteBatch> _drawNotifications;
        private static Action<SpriteBatch> _drawPlayerList;
        private static Action<SpriteBatch> _drawHitboxes;
        private static Action<Player, bool, Rectangle> _captureSwingHitbox;
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
        private static Func<bool> _isBetterChatActive;
        private static Func<string, bool, string> _processPlayerChatInput;
        private static Func<string, string> _formatPlayerChatText;
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
        private static string _lastDiagnostic;
        private static bool _shutdownHooked;

        /// <summary>Latest bridge availability or failure diagnostic for support and crash reports.</summary>
        public static string LastBridgeDiagnostic { get { return _lastDiagnostic ?? string.Empty; } }

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
                if (!EnsureBridge()) return;
                if (!_shutdownHooked)
                {
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => ShutdownPluginRuntime();
                    _shutdownHooked = true;
                }
                var bridgeType = _bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", false);
                if (bridgeType == null) return;
                var bootstrap = bridgeType.GetMethod("BootstrapPluginRuntime", BindingFlags.Public | BindingFlags.Static);
                bootstrap?.Invoke(null, null);
            }
            catch (Exception exception) { RecordFailure("Plugin runtime startup", exception); }
        }

        private static void ShutdownPluginRuntime()
        {
            try
            {
                var bridgeType = _bridgeAssembly?.GetType("AlacrityTerraria.PluginUiRuntime", false);
                bridgeType?.GetMethod("ShutdownPluginRuntime", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
            catch (Exception exception) { RecordFailure("Plugin runtime shutdown", exception); }
        }

        public static void OpenPluginManager()
        {
            try
            {
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
                if (EnsureBridge())
                {
                    if (!Main.gameMenu)
                    {
                        _drawNotifications?.Invoke(spriteBatch);
                        _drawPlayerList?.Invoke(spriteBatch);
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
                if (EnsureBridge())
                    _updatePluginKeybinds?.Invoke();
            }
            catch (Exception exception)
            {
                RecordFailure("Update plugin keybinds", exception);
            }
        }

        /// <summary>Forwards the verified world draw phase to the optional host-owned Hitboxes renderer.</summary>
        public static void DrawHitboxes(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || Main.gameMenu)
                return;
            try
            {
                if (!EnsureBridge() || _drawHitboxes == null)
                    return;
                _drawHitboxes(spriteBatch);
            }
            catch (Exception exception)
            {
                RecordFailure("Draw hitboxes", exception);
            }
        }

        /// <summary>Receives a vanilla-computed melee hitbox only when the optional diagnostics bridge is available.</summary>
        public static void CaptureSwingHitbox(Player player, bool dontAttack, Rectangle hitbox)
        {
            try
            {
                // This is called from a combat-hot path. Once resolved, avoid even the bridge readiness check.
                Action<Player, bool, Rectangle> capture = _captureSwingHitbox;
                if (capture != null)
                    capture(player, dontAttack, hitbox);
                else if (EnsureBridge())
                    _captureSwingHitbox?.Invoke(player, dontAttack, hitbox);
            }
            catch (Exception exception)
            {
                RecordFailure("Capture swing hitbox", exception);
            }
        }

        /// <summary>Runs before Terraria copies native key states so plugin trigger IDs exist in both old and current sets.</summary>
        public static void EnsurePluginKeybindStateShape()
        {
            try
            {
                BootstrapPluginRuntime();
                if (EnsureBridge())
                    _ensurePluginKeybindStateShape?.Invoke();
            }
            catch (Exception exception)
            {
                RecordFailure("Synchronize plugin keybind state", exception);
            }
        }

        // These version-locked calls fail open: an unavailable plugin bridge must never suppress vanilla effects.
        public static bool ShouldRunDustSystem() => EnsureBridge() && _shouldRunDustSystem != null ? _shouldRunDustSystem() : true;
        public static bool ShouldCreateDust(int dustType) => EnsureBridge() && _shouldCreateDust != null ? _shouldCreateDust(dustType) : true;
        public static bool ShouldUpdateDustInstance(Dust dust) => EnsureBridge() && _shouldUpdateDustInstance != null ? _shouldUpdateDustInstance(dust) : true;
        public static bool ShouldDrawDustInstance(Dust dust) => EnsureBridge() && _shouldDrawDustInstance != null ? _shouldDrawDustInstance(dust) : true;
        public static bool ShouldRunGoreSystem() => EnsureBridge() && _shouldRunGoreSystem != null ? _shouldRunGoreSystem() : true;

        public static bool TryHandlePluginChatCommand(string text)
        {
            return EnsureBridge() && _tryHandlePluginChatCommand != null && _tryHandlePluginChatCommand(text);
        }

        /// <summary>Version-locked controls-menu entry point. It remains a no-op when the optional bridge is unavailable.</summary>
        public static void AppendPluginKeybindControls(UIManageControls controls)
        {
            if (controls == null)
                return;

            try
            {
                BootstrapPluginRuntime();
                if (EnsureBridge())
                    _appendPluginKeybindControls?.Invoke(controls);
            }
            catch (Exception exception)
            {
                RecordFailure("Append plugin keybind controls", exception);
            }
        }

        // These methods are called only from version-locked chat IL patches. They remain no-ops
        // when the optional Core bridge or BetterChat package is unavailable.
        public static bool IsBetterChatActive()
        {
            return EnsureChatBridge() && _isBetterChatActive != null && _isBetterChatActive();
        }

        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            return EnsureChatBridge() && _processPlayerChatInput != null ? _processPlayerChatInput(text, allowMultiLine) : text;
        }

        public static string FormatPlayerChatText(string text)
        {
            if (EnsureChatBridge() && _formatPlayerChatText != null)
                return _formatPlayerChatText(text);
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
            if (_open != null)
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
                Type bridgeType = _bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", false);
                if (bridgeType == null)
                {
                    RecordUnavailable("Unavailable: the UI bridge does not contain AlacrityTerraria.PluginUiRuntime.");
                    return false;
                }

                string diagnostic;
                MethodInfo open;
                MethodInfo openIngame;
                MethodInfo drawIngame;
                MethodInfo drawNotifications;
                MethodInfo handleInput;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "Open", typeof(void), Type.EmptyTypes, out open, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "OpenIngamePluginSettings", typeof(void), Type.EmptyTypes, out openIngame, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "DrawIngamePluginSettings", typeof(void), new[] { typeof(SpriteBatch) }, out drawIngame, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "DrawNotifications", typeof(void), new[] { typeof(SpriteBatch) }, out drawNotifications, out diagnostic) ||
                    !Reflection.TryResolveStaticMethod(bridgeType, "HandlePluginMenuInput", typeof(bool), Type.EmptyTypes, out handleInput, out diagnostic))
                {
                    RecordUnavailable(diagnostic);
                    ClearBridgeDelegates();
                    return false;
                }

                Delegate callback;
                if (!Reflection.TryCreateDelegate(open, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _open = (Action)callback;
                if (!Reflection.TryCreateDelegate(openIngame, typeof(Action), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _openIngamePluginSettings = (Action)callback;
                if (!Reflection.TryCreateDelegate(drawIngame, typeof(Action<SpriteBatch>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _drawIngamePluginSettings = (Action<SpriteBatch>)callback;
                if (!Reflection.TryCreateDelegate(drawNotifications, typeof(Action<SpriteBatch>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _drawNotifications = (Action<SpriteBatch>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "DrawPlayerList", typeof(void), new[] { typeof(SpriteBatch) }, out var drawPlayerList, out _) &&
                    Reflection.TryCreateDelegate(drawPlayerList, typeof(Action<SpriteBatch>), out callback, out _))
                    _drawPlayerList = (Action<SpriteBatch>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "DrawHitboxes", typeof(void), new[] { typeof(SpriteBatch) }, out var drawHitboxes, out _) &&
                    Reflection.TryCreateDelegate(drawHitboxes, typeof(Action<SpriteBatch>), out callback, out _))
                    _drawHitboxes = (Action<SpriteBatch>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "CaptureSwingHitbox", typeof(void), new[] { typeof(Player), typeof(bool), typeof(Rectangle) }, out var captureSwingHitbox, out _) &&
                    Reflection.TryCreateDelegate(captureSwingHitbox, typeof(Action<Player, bool, Rectangle>), out callback, out _))
                    _captureSwingHitbox = (Action<Player, bool, Rectangle>)callback;
                if (!Reflection.TryCreateDelegate(handleInput, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _handlePluginMenuInput = (Func<bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "UpdatePluginKeybinds", typeof(void), Type.EmptyTypes, out var updatePluginKeybinds, out _) &&
                    Reflection.TryCreateDelegate(updatePluginKeybinds, typeof(Action), out callback, out _))
                    _updatePluginKeybinds = (Action)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "EnsurePluginKeybindStateShape", typeof(void), Type.EmptyTypes, out var ensurePluginKeybindStateShape, out _) &&
                    Reflection.TryCreateDelegate(ensurePluginKeybindStateShape, typeof(Action), out callback, out _))
                    _ensurePluginKeybindStateShape = (Action)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "AppendPluginKeybindControls", typeof(void), new[] { typeof(UIManageControls) }, out var appendPluginKeybindControls, out _) &&
                    Reflection.TryCreateDelegate(appendPluginKeybindControls, typeof(Action<UIManageControls>), out callback, out _))
                    _appendPluginKeybindControls = (Action<UIManageControls>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "ShouldRunDustSystem", typeof(bool), Type.EmptyTypes, out var shouldRunDustSystem, out _) &&
                    Reflection.TryCreateDelegate(shouldRunDustSystem, typeof(Func<bool>), out callback, out _))
                    _shouldRunDustSystem = (Func<bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "ShouldCreateDust", typeof(bool), new[] { typeof(int) }, out var shouldCreateDust, out _) &&
                    Reflection.TryCreateDelegate(shouldCreateDust, typeof(Func<int, bool>), out callback, out _))
                    _shouldCreateDust = (Func<int, bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "ShouldUpdateDustInstance", typeof(bool), new[] { typeof(Dust) }, out var shouldUpdateDustInstance, out _) &&
                    Reflection.TryCreateDelegate(shouldUpdateDustInstance, typeof(Func<Dust, bool>), out callback, out _))
                    _shouldUpdateDustInstance = (Func<Dust, bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "ShouldDrawDustInstance", typeof(bool), new[] { typeof(Dust) }, out var shouldDrawDustInstance, out _) &&
                    Reflection.TryCreateDelegate(shouldDrawDustInstance, typeof(Func<Dust, bool>), out callback, out _))
                    _shouldDrawDustInstance = (Func<Dust, bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "ShouldRunGoreSystem", typeof(bool), Type.EmptyTypes, out var shouldRunGoreSystem, out _) &&
                    Reflection.TryCreateDelegate(shouldRunGoreSystem, typeof(Func<bool>), out callback, out _))
                    _shouldRunGoreSystem = (Func<bool>)callback;
                if (Reflection.TryResolveStaticMethod(bridgeType, "TryHandlePluginChatCommand", typeof(bool), new[] { typeof(string) }, out var tryHandlePluginChatCommand, out _) &&
                    Reflection.TryCreateDelegate(tryHandlePluginChatCommand, typeof(Func<string, bool>), out callback, out _))
                    _tryHandlePluginChatCommand = (Func<string, bool>)callback;
                return true;
            }
            catch (Exception exception)
            {
                ClearBridgeDelegates();
                _bridgeAssembly = null;
                RecordFailure("Load UI bridge", exception);
                return false;
            }
        }

        private static bool EnsureChatBridge()
        {
            if (_chatBridgeResolved)
                return _isBetterChatActive != null;
            _chatBridgeResolved = true;
            if (!EnsureBridge())
                return false;

            try
            {
                Type bridgeType = _bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", false);
                string diagnostic;
                MethodInfo method;
                Delegate callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "IsBetterChatActive", typeof(bool), Type.EmptyTypes, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); return false; }
                _isBetterChatActive = (Func<bool>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "ProcessPlayerChatInput", typeof(string), new[] { typeof(string), typeof(bool) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<string, bool, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _processPlayerChatInput = (Func<string, bool, string>)callback;
                if (!Reflection.TryResolveStaticMethod(bridgeType, "FormatPlayerChatText", typeof(string), new[] { typeof(string) }, out method, out diagnostic) || !Reflection.TryCreateDelegate(method, typeof(Func<string, string>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearChatDelegates(); return false; }
                _formatPlayerChatText = (Func<string, string>)callback;
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
                RecordFailure("Resolve BetterChat bridge", exception);
                ClearChatDelegates();
                return false;
            }
        }

        private static void ClearBridgeDelegates()
        {
            _open = null;
            _openIngamePluginSettings = null;
            _drawIngamePluginSettings = null;
            _drawNotifications = null;
            _drawPlayerList = null;
            _drawHitboxes = null;
            _captureSwingHitbox = null;
            _updatePluginKeybinds = null;
            _ensurePluginKeybindStateShape = null;
            _appendPluginKeybindControls = null;
            _shouldRunDustSystem = null;
            _shouldCreateDust = null;
            _shouldUpdateDustInstance = null;
            _shouldDrawDustInstance = null;
            _shouldRunGoreSystem = null;
            _tryHandlePluginChatCommand = null;
            _handlePluginMenuInput = null;
            ClearChatDelegates();
        }

        private static void ClearChatDelegates()
        {
            _isBetterChatActive = null;
            _processPlayerChatInput = null;
            _formatPlayerChatText = null;
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
