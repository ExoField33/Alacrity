using System;
using System.Linq;
using Alacrity.App.PluginManagement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;

namespace AlacrityTerraria
{
    public static partial class PluginUiRuntime
    {
        public static void Open()
        {
            EnsurePluginManager();
            RefreshPluginCatalog();
            Main.menuMode = 888;
            _selectionMenu = new PluginSelectionMenu(_menu);
            Main.MenuUI.SetState(_selectionMenu);
            _pluginMenuOpen = true;

            // DrawMenu activates the Plugins row with the same pointer press that creates this
            // UI state. Do not let that already-consumed press bubble into a newly-created menu
            // control on its first frame; future input frames remain entirely native.
            Main.mouseLeftRelease = false;
            Main.mouseRightRelease = false;
        }

        public static bool HandlePluginMenuInput()
        {
            if (TryHandleIngamePluginEscape())
            {
                return false;
            }

            if (!_pluginMenuOpen || Main.menuMode != 888 || !IsEscapeJustPressed())
                return true;

            if (Main.MenuUI.CurrentState is PluginSettingsMenu settingsMenu)
            {
                if (!settingsMenu.TryCloseDropdown())
                {
                    ReturnToPluginList();
                }
            }
            else if (_selectionMenu != null && Main.MenuUI.CurrentState is PluginDescriptionMenu)
                ReturnToPluginList();
            else
                Close();
            return false;
        }

        private static bool TryHandleIngamePluginEscape()
        {
            if (!IsEscapeJustPressed())
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_ingameOpenDropdownControlId))
            {
                if (_ingameDropdownSearchFocused)
                {
                    StopIngameDropdownSearchInput();
                }
                else
                {
                    CloseIngameDropdown(playSound: true);
                }
            }
            else if (_ingameView != 0)
            {
                _ingameView = 0;
                _ingameDescriptionScroll = 0f;
                _ingameHoveredSettingId = null;
                _ingameHoveredPluginActionId = null;
                SoundEngine.PlaySound(11, -1, -1, 1, 1f, 0f);
            }
            else
            {
                return false;
            }

            // This runs at Terraria's input boundary, before Player can interpret Escape as a
            // request to close the entire in-game options panel.
            Main.inputTextEscape = false;
            Main.keyCount = 0;
            return true;
        }

        private static bool IsEscapeJustPressed()
        {
            if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
            {
                return true;
            }

            int count = Math.Max(0, Math.Min(Main.keyCount, Main.keyInt.Length));
            for (int index = 0; index < count; index++)
            {
                if (Main.keyInt[index] == 27)
                {
                    return true;
                }
            }

            return false;
        }

        public static void OpenIngamePluginSettings()
        {
            EnsurePluginManager();
            RefreshPluginCatalog();
            _ingameEntries = _presenter.Present(_runtime, _diagnostics.ActiveWarnings)
                .Where(entry => entry.IsEnabled)
                .ToArray();
            _ingameSelectedEntry = 0;
            _ingameView = 0;
            _ingameScroll = 0f;
            _ingameDescriptionScroll = 0f;
            _ingameHoveredSettingId = null;
            _ingameHoveredPluginActionId = null;
            _ingamePointerCaptureId = null;
            _ingameOpenDropdownControlId = null;
            _ingameDropdownScroll = 0;
            _ingameDropdownSearchText = string.Empty;
            _ingameDropdownSearchFocused = false;
            _ingameDropdownFilteredOptions.Clear();
        }

        public static void DrawIngamePluginSettings(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null)
                return;

            UpdateIngamePointerCapture();

            // IngameOptions.Draw creates the standard right settings pane at this exact location:
            // 670x480 window, right half inset by 20px horizontally and 50px vertically.
            // Match that pane instead of approximating its center so plugin rows remain contained.
            var bounds = new Rectangle(Main.screenWidth / 2 + 20, Main.screenHeight / 2 - 190, 305, 420);
            if (_ingameEntries.Length == 0)
            {
                Utils.DrawBorderString(spriteBatch, "No plugins installed.", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.75f, 0.5f, 0.5f, -1);
                return;
            }

            if (_ingameView == 1)
                DrawIngamePluginDescription(spriteBatch, bounds, _ingameEntries[_ingameSelectedEntry]);
            else if (_ingameView == 2)
                DrawIngamePluginSettingsPage(spriteBatch, bounds, _ingameEntries[_ingameSelectedEntry]);
            else
                DrawIngamePluginList(spriteBatch, bounds);
        }

        /// <summary>Renders active host notifications through Terraria's established gameplay UI SpriteBatch.</summary>

        private static void ReturnToPluginList()
        {
            if (_selectionMenu != null)
                Main.MenuUI.SetState(_selectionMenu);
        }

        private static void Close()
        {
            _pluginMenuOpen = false;
            _selectionMenu = null;
            Main.menuMode = 0;
            Main.MenuUI.SetState(null);
        }
    }
}
