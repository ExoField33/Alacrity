using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using Terraria;

namespace AlacrityTerraria
{
    // The injected entry point must stay independent of the SDK/Core assemblies.
    // They are loaded only after the user opens Plugins, so an unavailable plugin
    // dependency cannot prevent Terraria from reaching its normal main menu.
    public static class PluginUiRuntime
    {
        private static FieldInfo _menuMode;
        private static FieldInfo _versionNumber;
        private static Assembly _bridgeAssembly;
        private static MethodInfo _open;
        private static MethodInfo _openIngamePluginSettings;
        private static MethodInfo _drawIngamePluginSettings;
        private static MethodInfo _drawNotifications;
        private static MethodInfo _handlePluginMenuInput;
        private static Action<Color, float> _drawVersionNumber;
        private static bool _versionRendererResolved;
        private static bool _bridgeLoadAttempted;
        private static FieldInfo _ingameOptionsCategory;
        private const int IngamePluginsCategory = 777016;

        public static bool HandleInput()
        {
            try
            {
                FieldInfo menuMode = GetMenuModeField();
                if (menuMode == null)
                    return true;

                int currentMenu = ReadMenuMode(menuMode);
                if (currentMenu == 888)
                    return HandlePluginMenuInput();
                return true;
            }
            catch
            {
                SetMenuMode(GetMenuModeField(), 0);
                return true;
            }
        }

        public static void OpenPluginManager()
        {
            try
            {
                if (EnsureBridge())
                {
                    SoundEngine.PlaySound(10, -1, -1, 1, 1f, 0f);
                    _open.Invoke(null, null);
                }
            }
            catch
            {
                // A failed optional UI bridge must leave the native main menu usable.
            }
        }

        public static void DrawAlacrityVersion(Color color, float verticalOffset, string versionText)
        {
            if (string.IsNullOrWhiteSpace(versionText))
                return;

            try
            {
                if (!EnsureVersionRenderer())
                    return;

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
            catch
            {
                // Optional branding must never disrupt Terraria's native version rendering.
            }
        }

        public static void OpenIngamePluginSettings()
        {
            try
            {
                SetIngamePluginsCategory();
                if (!EnsureBridge() || _openIngamePluginSettings == null)
                {
                    RestoreIngameOptionsCategory();
                    return;
                }

                _openIngamePluginSettings.Invoke(null, null);
            }
            catch
            {
                // A failed optional category must return to vanilla settings instead of a blank panel.
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

                _drawIngamePluginSettings.Invoke(null, new object[] { spriteBatch });
            }
            catch
            {
                // The vanilla settings screen remains available if the optional panel fails.
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
                    _drawNotifications.Invoke(null, new object[] { spriteBatch });
            }
            catch
            {
                // Notifications are optional presentation and must never interrupt Terraria UI drawing.
            }
        }

        private static bool EnsureVersionRenderer()
        {
            if (_versionRendererResolved)
                return _drawVersionNumber != null && _versionNumber != null;

            _versionRendererResolved = true;
            _versionNumber = typeof(Main).GetField("versionNumber", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo renderer = typeof(Main).GetMethod(
                "DrawVersionNumber",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Color), typeof(float) },
                null);
            if (_versionNumber == null || renderer == null)
                return false;

            _drawVersionNumber = (Action<Color, float>)Delegate.CreateDelegate(typeof(Action<Color, float>), renderer);
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
                return false;

            try
            {
                _bridgeAssembly = Assembly.LoadFrom(path);
                Type bridgeType = _bridgeAssembly.GetType("AlacrityTerraria.PluginUiRuntime", true);
                _open = bridgeType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);
                _openIngamePluginSettings = bridgeType.GetMethod("OpenIngamePluginSettings", BindingFlags.Public | BindingFlags.Static);
                _drawIngamePluginSettings = bridgeType.GetMethod("DrawIngamePluginSettings", BindingFlags.Public | BindingFlags.Static);
                _drawNotifications = bridgeType.GetMethod("DrawNotifications", BindingFlags.Public | BindingFlags.Static);
                _handlePluginMenuInput = bridgeType.GetMethod("HandlePluginMenuInput", BindingFlags.Public | BindingFlags.Static);
                return _open != null;
            }
            catch
            {
                _bridgeAssembly = null;
                _open = null;
                _openIngamePluginSettings = null;
                _drawIngamePluginSettings = null;
                _drawNotifications = null;
                _handlePluginMenuInput = null;
                return false;
            }
        }

        private static bool HandlePluginMenuInput()
        {
            if (!EnsureBridge() || _handlePluginMenuInput == null)
                return true;

            return (bool)_handlePluginMenuInput.Invoke(null, null);
        }

        private static bool IsIngamePluginsCategory()
        {
            FieldInfo category = GetIngameOptionsCategoryField();
            return category != null && (int)category.GetValue(null) == IngamePluginsCategory;
        }

        private static void SetIngamePluginsCategory()
        {
            FieldInfo category = GetIngameOptionsCategoryField();
            if (category != null)
                category.SetValue(null, IngamePluginsCategory);
        }

        private static void RestoreIngameOptionsCategory()
        {
            FieldInfo category = GetIngameOptionsCategoryField();
            if (category != null)
                category.SetValue(null, 0);
        }

        private static FieldInfo GetIngameOptionsCategoryField()
        {
            return _ingameOptionsCategory ?? (_ingameOptionsCategory = typeof(IngameOptions).GetField(
                "category",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        }

        private static FieldInfo GetMenuModeField()
        {
            return _menuMode ?? (_menuMode = typeof(Main).GetField(
                "menuMode",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        }

        private static int ReadMenuMode(FieldInfo field)
        {
            return field == null ? 0 : (int)field.GetValue(null);
        }

        private static void SetMenuMode(FieldInfo field, int value)
        {
            if (field != null)
                field.SetValue(null, value);
        }

    }
}
