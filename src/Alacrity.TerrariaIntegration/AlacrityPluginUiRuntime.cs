using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria;

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
        private static Func<bool> _handlePluginMenuInput;
        private static Action<Color, float> _drawVersionNumber;
        private static bool _versionRendererResolved;
        private static bool _bridgeLoadAttempted;
        private static string _lastDiagnostic;

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
            if (spriteBatch == null || Main.gameMenu)
                return;

            try
            {
                if (EnsureBridge() && _drawNotifications != null)
                    _drawNotifications(spriteBatch);
            }
            catch (Exception exception)
            {
                RecordFailure("Draw plugin notifications", exception);
            }
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
                if (!Reflection.TryCreateDelegate(handleInput, typeof(Func<bool>), out callback, out diagnostic)) { RecordUnavailable(diagnostic); ClearBridgeDelegates(); return false; }
                _handlePluginMenuInput = (Func<bool>)callback;
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

        private static void ClearBridgeDelegates()
        {
            _open = null;
            _openIngamePluginSettings = null;
            _drawIngamePluginSettings = null;
            _drawNotifications = null;
            _handlePluginMenuInput = null;
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
