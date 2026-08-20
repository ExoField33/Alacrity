using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;

namespace AlacrityTerraria
{
    /// <summary>
    /// In-game settings-page rendering and immediate-mode helper implementation. This remains a
    /// partial bridge type only because the existing patched ABI calls its facade methods.
    /// </summary>
    public static partial class PluginUiRuntime
    {
        private static void DrawIngamePluginSettingsPage(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name + " Settings", new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            var controls = _extensions.GetSettingsControls(plugin.Id);
            var pages = _extensions.GetSettingsPages(plugin.Id).Where(page => page.IsInteractive).ToArray();
            if (!string.IsNullOrEmpty(_ingameOpenDropdownControlId) && !ContainsIngameControl(controls, _ingameOpenDropdownControlId))
            {
                CloseIngameDropdown(playSound: false);
            }

            if (controls.Count == 0 && pages.Length == 0)
            {
                Utils.DrawBorderString(spriteBatch, "No plugin settings are available.", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.7f, 0.5f, 0.5f, -1);
                return;
            }

            int y = bounds.Y + 58;
            bool anySettingHovered = false;
            foreach (var control in controls)
            {
                y += DrawIngameTypedControl(spriteBatch, bounds, y, control, ref anySettingHovered);
            }
            foreach (var page in pages)
            {
                var hitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
                bool hovered = CanInteractWithIngameSettings && hitArea.Contains(Main.mouseX, Main.mouseY);
                anySettingHovered |= hovered;
                if (hovered && _ingameHoveredSettingId != page.Id)
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                if (hovered)
                    _ingameHoveredSettingId = page.Id;

                string value = ReadSettingValue(page);
                Utils.DrawBorderString(
                    spriteBatch,
                    page.DisplayName + ": " + value,
                    new Vector2(bounds.Center.X, y),
                    hovered ? new Color(255, 230, 140) : Color.White,
                    hovered ? 0.8f : 0.7f,
                    0.5f,
                    0f,
                    -1);

                if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
                    ActivateSetting(page);
                y += 30;
            }

            if (!anySettingHovered)
                _ingameHoveredSettingId = null;

            DrawIngameDropdown(spriteBatch, bounds, controls, ref anySettingHovered);
        }

        private static int DrawIngameTypedControl(SpriteBatch spriteBatch, Rectangle bounds, int y, PluginSettingControl control, ref bool anyHovered)
        {
            if (control.Kind == PluginSettingControlKind.Dropdown)
            {
                var dropdownHitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
                bool dropdownHover = CanInteractWithIngameSettings && dropdownHitArea.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= dropdownHover;
                if (dropdownHover && _ingameHoveredSettingId != control.Id)
                {
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                }

                if (dropdownHover)
                {
                    _ingameHoveredSettingId = control.Id;
                }

                Utils.DrawBorderString(
                    spriteBatch,
                    control.DisplayName + ": " + ReadSettingValue(control) + "  >",
                    new Vector2(bounds.Center.X, y),
                    dropdownHover ? new Color(255, 230, 140) : Color.White,
                    dropdownHover ? 0.8f : 0.7f,
                    0.5f,
                    0f,
                    -1);
                if (dropdownHover && Main.mouseLeft && Main.mouseLeftRelease)
                {
                    OpenIngameDropdown(control);
                }

                return 30;
            }

            if (control.Kind == PluginSettingControlKind.Color)
            {
                var swatch = new Rectangle(bounds.X + 18, y - 4, 20, 20);
                Utils.DrawInvBG(spriteBatch, swatch.X, swatch.Y, swatch.Width, swatch.Height, new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue));
                Utils.DrawBorderString(spriteBatch, control.DisplayName, new Vector2(bounds.X + 46, y), Color.White, 0.7f, 0f, 0f, -1);
                var copy = new Rectangle(bounds.Right - 73, y - 5, 25, 22);
                var paste = new Rectangle(bounds.Right - 42, y - 5, 25, 22);
                bool allowHover = CanInteractWithIngameSettings;
                bool copyHover = allowHover && copy.Contains(Main.mouseX, Main.mouseY);
                bool pasteHover = allowHover && paste.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= copyHover || pasteHover;
                DrawIngameClipboardButton(spriteBatch, copy, "Images/UI/CharCreation/Copy", copyHover, "Copy color hex (" + control.GetColor().ToHex() + ")");
                DrawIngameClipboardButton(spriteBatch, paste, "Images/UI/CharCreation/Paste", pasteHover, pasteHover ? GetColorPasteTooltip() : "Paste color hex");
                if (Main.mouseLeft && Main.mouseLeftRelease && copyHover) TrySetClipboardText(control.GetColor().ToHex());
                if (Main.mouseLeft && Main.mouseLeftRelease && pasteHover && PluginColor.TryParseHex(TryGetClipboardText(), out var pasted)) control.SetColor(pasted);
                return 34;
            }
            if (control.Kind == PluginSettingControlKind.Slider)
            {
                var bar = new Rectangle(bounds.Right - 150, y - 2, 132, 14);
                // Only one plugin settings page is visible at a time, so the owner-local control
                // ID is enough to identify its primary-pointer capture without allocating a key.
                string captureId = control.Id;
                bool captured = IsIngamePointerCaptured(captureId);
                bool hovered = captured || (CanInteractWithIngameSettings && bar.Contains(Main.mouseX, Main.mouseY));
                if (!captured && hovered && Main.mouseLeft)
                {
                    BeginIngamePointerCapture(captureId);
                    captured = true;
                }

                anyHovered |= hovered || captured;
                DrawIngameSlider(spriteBatch, bar, NormalizeSlider(control));
                Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.X + 18, y), Color.White, 0.7f, 0f, 0f, -1);
                if (captured && Main.mouseLeft)
                {
                    SetIngameSliderValue(control, (Main.mouseX - bar.X) / (float)bar.Width);
                    ConsumeIngamePointer();
                }
                return 32;
            }
            var hitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
            bool hover = CanInteractWithIngameSettings && hitArea.Contains(Main.mouseX, Main.mouseY);
            anyHovered |= hover;
            if (hover && _ingameHoveredSettingId != control.Id) SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            if (hover) _ingameHoveredSettingId = control.Id;
            Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.Center.X, y), hover ? new Color(255, 230, 140) : Color.White, hover ? 0.8f : 0.7f, 0.5f, 0f, -1);
            if (hover && Main.mouseLeft && Main.mouseLeftRelease) ActivateSetting(control);
            return 30;
        }

        private static bool CanInteractWithIngameSettings => !HasIngamePointerCapture && string.IsNullOrEmpty(_ingameOpenDropdownControlId);

        private static bool ContainsIngameControl(IReadOnlyList<PluginSettingControl> controls, string id)
        {
            for (int index = 0; index < controls.Count; index++)
            {
                if (string.Equals(controls[index].Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void OpenIngameDropdown(PluginSettingControl control)
        {
            try
            {
                IReadOnlyList<PluginSettingOption> options = control.GetDropdownOptions();
                if (options == null || options.Count == 0)
                {
                    ShowHoverText("No choices are available for this setting.");
                    return;
                }

                _ingameOpenDropdownControlId = control.Id;
                _ingameDropdownScroll = 0;
                _ingameDropdownTop = -1;
                _ingameDropdownSearchText = string.Empty;
                _ingameDropdownSearchBuffer.Clear();
                StopIngameDropdownSearchInput();
                _ingameDropdownFilteredOptions.Clear();
                _ingameHoveredSettingId = null;
                SoundEngine.PlaySound(10, -1, -1, 1, 1f, 0f);
                ConsumeIngamePointer();
            }
            catch (Exception exception)
            {
                ShowHoverText("Unable to open plugin setting choices: " + exception.Message);
            }
        }

        private static void DrawIngameDropdown(SpriteBatch spriteBatch, Rectangle bounds, IReadOnlyList<PluginSettingControl> controls, ref bool anySettingHovered)
        {
            string openId = _ingameOpenDropdownControlId;
            if (string.IsNullOrEmpty(openId))
            {
                return;
            }

            PluginSettingControl control = null;
            for (int index = 0; index < controls.Count; index++)
            {
                if (string.Equals(controls[index].Id, openId, StringComparison.Ordinal))
                {
                    control = controls[index];
                    break;
                }
            }

            if (control == null)
            {
                CloseIngameDropdown(playSound: false);
                return;
            }

            IReadOnlyList<PluginSettingOption> options;
            string selected;
            try
            {
                options = control.GetDropdownOptions();
                selected = control.GetDropdown();
            }
            catch (Exception exception)
            {
                CloseIngameDropdown(playSound: false);
                ShowHoverText("Unable to read plugin setting choices: " + exception.Message);
                return;
            }

            if (options == null || options.Count == 0)
            {
                CloseIngameDropdown(playSound: false);
                return;
            }

            UpdateIngameDropdownSearchInput();
            PluginDropdownFilter.Filter(options, _ingameDropdownSearchText, _ingameDropdownFilteredOptions);

            const int rowHeight = 24;
            const int headerHeight = 26;
            const int searchHeight = 24;
            const int maximumRows = 10;
            int optionCount = _ingameDropdownFilteredOptions.Count;
            int visibleRows = Math.Min(maximumRows, Math.Max(1, optionCount));
            int menuHeight = headerHeight + searchHeight + visibleRows * rowHeight + 4;
            int menuWidth = Math.Min(240, Math.Max(180, bounds.Width - 54));
            if (_ingameDropdownTop < 0)
            {
                _ingameDropdownTop = Math.Max(bounds.Y + 34, bounds.Bottom - menuHeight - 12);
            }

            // The chooser is initially placed to fit its full option set. Filtering keeps this
            // anchor so results collapse below the search field instead of jumping upward.
            var menuBounds = new Rectangle(bounds.X + 24, _ingameDropdownTop, menuWidth, menuHeight);
            int maximumScroll = Math.Max(0, optionCount - visibleRows);
            if (menuBounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta == 0)
                {
                    delta = Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI;
                }
                if (delta != 0)
                {
                    _ingameDropdownScroll = Math.Max(0, Math.Min(maximumScroll, _ingameDropdownScroll - Math.Sign(delta)));
                    // This modal picker owns the wheel while hovered. It must not also select a
                    // hotbar item or leak into the underlying settings pane later in the frame.
                    Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
                    Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI = 0;
                }

                anySettingHovered = true;
                MarkIngameMouseInterface();
            }

            _ingameDropdownScroll = Math.Max(0, Math.Min(maximumScroll, _ingameDropdownScroll));
            DrawIngameDropdownScrollbar(spriteBatch, menuBounds, headerHeight + searchHeight, visibleRows, optionCount);
            var header = new Rectangle(menuBounds.X, menuBounds.Y, menuBounds.Width - 8, headerHeight);
            bool headerHover = header.Contains(Main.mouseX, Main.mouseY);
            DrawIngameDropdownHover(spriteBatch, header, headerHover);
            Utils.DrawBorderString(spriteBatch, "< " + control.DisplayName, new Vector2(header.X + 4, header.Center.Y), Color.White, headerHover ? 0.72f : 0.68f, 0f, 0.5f, -1);
            if (headerHover && Main.mouseLeft && Main.mouseLeftRelease)
            {
                CloseIngameDropdown(playSound: true);
                ConsumeIngamePointer();
                return;
            }

            var search = new Rectangle(menuBounds.X, menuBounds.Y + headerHeight, menuBounds.Width - 8, searchHeight);
            bool searchHover = search.Contains(Main.mouseX, Main.mouseY);
            DrawIngameDropdownHover(spriteBatch, search, searchHover || _ingameDropdownSearchFocused);
            DrawIngameDropdownSearchSelection(spriteBatch, search, 0.7f);
            string searchText = string.IsNullOrEmpty(_ingameDropdownSearchText) ? "Search languages..." : "Search: " + _ingameDropdownSearchText;
            Color searchColor = string.IsNullOrEmpty(_ingameDropdownSearchText) ? Color.Gray : Color.White;
            if (_ingameDropdownSearchFocused && Main.instance != null && Main.instance.textBlinkerState == 1)
            {
                searchText = InsertSearchCaret(searchText, _ingameDropdownSearchBuffer.Caret);
            }

            Utils.DrawBorderString(spriteBatch, searchText, new Vector2(search.X + 4, search.Center.Y), searchColor, searchHover || _ingameDropdownSearchFocused ? 0.7f : 0.66f, 0f, 0.5f, -1);
            if (searchHover && Main.mouseLeft && Main.mouseLeftRelease)
            {
                if (!_ingameDropdownSearchFocused)
                {
                    _ingameDropdownSearchFocused = true;
                    Main.clrInput();
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                }

                ConsumeIngamePointer();
                return;
            }

            for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
            {
                int optionIndex = _ingameDropdownScroll + rowIndex;
                if (optionIndex >= optionCount)
                {
                    break;
                }

                PluginSettingOption option = _ingameDropdownFilteredOptions[optionIndex];
                var row = new Rectangle(menuBounds.X, menuBounds.Y + headerHeight + searchHeight + rowIndex * rowHeight, menuBounds.Width - 8, rowHeight);
                bool hovered = row.Contains(Main.mouseX, Main.mouseY);
                if (hovered && _ingameHoveredSettingId != "dropdown:" + option.Value)
                {
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                }

                if (hovered)
                {
                    _ingameHoveredSettingId = "dropdown:" + option.Value;
                    anySettingHovered = true;
                }

                bool selectedOption = string.Equals(option.Value, selected, StringComparison.OrdinalIgnoreCase);
                DrawIngameDropdownHover(spriteBatch, row, hovered);
                Utils.DrawBorderString(spriteBatch, option.DisplayName, new Vector2(row.X + 4, row.Center.Y), selectedOption ? new Color(191, 219, 255) : Color.White, hovered ? 0.7f : 0.66f, 0f, 0.5f, -1);
                if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
                {
                    try
                    {
                        control.SetDropdown(option.Value);
                        CloseIngameDropdown(playSound: true);
                        ConsumeIngamePointer();
                    }
                    catch (Exception exception)
                    {
                        ShowHoverText("Unable to change plugin setting: " + exception.Message);
                    }

                    return;
                }
            }

            if (optionCount == 0)
            {
                var empty = new Rectangle(menuBounds.X, menuBounds.Y + headerHeight + searchHeight, menuBounds.Width - 8, rowHeight);
                Utils.DrawBorderString(spriteBatch, "No matching languages.", new Vector2(empty.X + 4, empty.Center.Y), Color.Gray, 0.66f, 0f, 0.5f, -1);
            }

            if (!menuBounds.Contains(Main.mouseX, Main.mouseY) && Main.mouseLeft && Main.mouseLeftRelease)
            {
                CloseIngameDropdown(playSound: true);
                ConsumeIngamePointer();
            }
        }

        private static void DrawIngameDropdownHover(SpriteBatch spriteBatch, Rectangle bounds, bool highlighted)
        {
            if (!highlighted)
            {
                return;
            }

            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture != null)
            {
                spriteBatch.Draw(_ingameBlankTexture, new Rectangle(bounds.X - 2, bounds.Y + 2, bounds.Width + 2, Math.Max(1, bounds.Height - 4)), new Color(116, 154, 236, 52));
            }
        }

        private static void DrawIngameDropdownScrollbar(SpriteBatch spriteBatch, Rectangle menuBounds, int contentTop, int visibleRows, int optionCount)
        {
            if (optionCount <= visibleRows)
            {
                return;
            }

            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null)
            {
                return;
            }

            int trackHeight = visibleRows * 24 - 4;
            int trackX = menuBounds.Right - 8;
            int trackY = menuBounds.Y + contentTop + 2;
            int thumbHeight = Math.Max(18, trackHeight * visibleRows / optionCount);
            int maximumScroll = optionCount - visibleRows;
            int thumbY = trackY + (trackHeight - thumbHeight) * _ingameDropdownScroll / maximumScroll;
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(trackX, trackY, 3, trackHeight), new Color(18, 12, 58, 180));
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(trackX - 1, thumbY, 5, thumbHeight), new Color(180, 170, 255, 220));
        }

        private static void UpdateIngameDropdownSearchInput()
        {
            if (!_ingameDropdownSearchFocused)
            {
                if (!ShouldStartIngameDropdownSearch(Main.inputText, Main.oldInputText))
                {
                    return;
                }

                _ingameDropdownSearchFocused = true;
            }

            Terraria.GameInput.PlayerInput.WritingText = true;
            Main.instance?.HandleIME();
            Main.inputTextEscape = false;
            Main.inputTextEnter = false;
            ProcessIngameDropdownSearchKeys();
            string updated = _ingameDropdownSearchBuffer.Text;
            if (Main.inputTextEscape)
            {
                Main.inputTextEscape = false;
                StopIngameDropdownSearchInput();
                return;
            }

            if (Main.inputTextEnter)
            {
                Main.inputTextEnter = false;
                StopIngameDropdownSearchInput();
            }

            if (!string.Equals(updated, _ingameDropdownSearchText, StringComparison.Ordinal))
            {
                _ingameDropdownSearchText = updated;
                _ingameDropdownScroll = 0;
            }
        }

        private static void ProcessIngameDropdownSearchKeys()
        {
            KeyboardState current = Main.inputText;
            KeyboardState previous = Main.oldInputText;
            bool control = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
            bool shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
            if (Pressed(current, previous, Keys.Escape))
            {
                Main.inputTextEscape = true;
            }
            else if (Pressed(current, previous, Keys.Enter))
            {
                Main.inputTextEnter = true;
            }
            else if (control && Pressed(current, previous, Keys.A))
            {
                _ingameDropdownSearchBuffer.SelectAll();
            }
            else if (_ingameDropdownBackspaceRepeat.ShouldRepeat(current, previous, Keys.Back))
            {
                _ingameDropdownSearchBuffer.Backspace(control);
            }
            else if (_ingameDropdownDeleteRepeat.ShouldRepeat(current, previous, Keys.Delete))
            {
                _ingameDropdownSearchBuffer.Delete(control);
            }
            else if (_ingameDropdownLeftRepeat.ShouldRepeat(current, previous, Keys.Left))
            {
                _ingameDropdownSearchBuffer.MoveLeft(control, shift);
            }
            else if (_ingameDropdownRightRepeat.ShouldRepeat(current, previous, Keys.Right))
            {
                _ingameDropdownSearchBuffer.MoveRight(control, shift);
            }
            else if (Pressed(current, previous, Keys.Home))
            {
                _ingameDropdownSearchBuffer.MoveHome(shift);
            }
            else if (Pressed(current, previous, Keys.End))
            {
                _ingameDropdownSearchBuffer.MoveEnd(shift);
            }
            else
            {
                int count = Math.Max(0, Math.Min(Main.keyCount, Math.Min(Main.keyInt.Length, Main.keyString.Length)));
                for (int index = 0; index < count; index++)
                {
                    int key = Main.keyInt[index];
                    string value = Main.keyString[index];
                    if (key >= 32 && key != 127 && !string.IsNullOrEmpty(value))
                    {
                        _ingameDropdownSearchBuffer.Insert(value);
                    }
                }
            }

            Main.keyCount = 0;
            Main.oldInputText = current;
            Main.inputText = Keyboard.GetState();
        }

        private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key)
        {
            return current.IsKeyDown(key) && !previous.IsKeyDown(key);
        }

        private static bool ShouldStartIngameDropdownSearch(KeyboardState current, KeyboardState previous)
        {
            if (Pressed(current, previous, Keys.Escape) ||
                current.IsKeyDown(Keys.Escape) ||
                previous.IsKeyDown(Keys.Escape) ||
                Pressed(current, previous, Keys.Enter))
            {
                return false;
            }

            int count = Math.Max(0, Math.Min(Main.keyCount, Main.keyInt.Length));
            for (int index = 0; index < count; index++)
            {
                int key = Main.keyInt[index];
                if (key >= 32 && key != 127)
                {
                    return true;
                }
            }

            return false;
        }

        private static string InsertSearchCaret(string display, int caret)
        {
            const string prefix = "Search: ";
            if (!display.StartsWith(prefix, StringComparison.Ordinal))
            {
                return display + "|";
            }

            int index = Math.Max(prefix.Length, Math.Min(display.Length, prefix.Length + caret));
            return display.Insert(index, "|");
        }

        private static void DrawIngameDropdownSearchSelection(SpriteBatch spriteBatch, Rectangle search, float scale)
        {
            if (!_ingameDropdownSearchFocused ||
                !_ingameDropdownSearchBuffer.TryGetSelection(out int start, out int end))
            {
                return;
            }

            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null)
            {
                return;
            }

            string value = _ingameDropdownSearchBuffer.Text;
            Vector2 left = Utils.DrawBorderString(spriteBatch, "Search: " + value.Substring(0, start), Vector2.Zero, Color.Transparent, scale);
            Vector2 right = Utils.DrawBorderString(spriteBatch, "Search: " + value.Substring(0, end), Vector2.Zero, Color.Transparent, scale);
            int width = Math.Max(1, (int)Math.Ceiling(right.X - left.X));
            int height = Math.Max(1, (int)Math.Ceiling(Utils.DrawBorderString(spriteBatch, " ", Vector2.Zero, Color.Transparent, scale).Y));
            var selection = new Rectangle(search.X + 4 + (int)left.X, search.Center.Y - height / 2, width, height);
            spriteBatch.Draw(_ingameBlankTexture, selection, new Color(96, 142, 218, 150));
        }

        private static void CloseIngameDropdown(bool playSound)
        {
            _ingameOpenDropdownControlId = null;
            _ingameDropdownScroll = 0;
            _ingameDropdownTop = -1;
            _ingameDropdownSearchText = string.Empty;
            _ingameDropdownSearchBuffer.Clear();
            _ingameDropdownBackspaceRepeat = default;
            _ingameDropdownDeleteRepeat = default;
            _ingameDropdownLeftRepeat = default;
            _ingameDropdownRightRepeat = default;
            StopIngameDropdownSearchInput();
            _ingameDropdownFilteredOptions.Clear();
            _ingameHoveredSettingId = null;
            if (playSound)
            {
                SoundEngine.PlaySound(11, -1, -1, 1, 1f, 0f);
            }
        }

        private static void StopIngameDropdownSearchInput()
        {
            _ingameDropdownSearchFocused = false;
            Terraria.GameInput.PlayerInput.WritingText = false;
            Main.instance?.HandleIME();
        }

        private static void DrawIngameSlider(SpriteBatch spriteBatch, Rectangle bar, float value)
        {
            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null) return;
            spriteBatch.Draw(_ingameBlankTexture, bar, new Color(29, 36, 70, 220));
            int filledWidth = (int)((bar.Width - 4) * MathHelper.Clamp(value, 0f, 1f));
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(bar.X + 2, bar.Y + 4, filledWidth, 6), new Color(160, 180, 255));
            spriteBatch.Draw(_ingameBlankTexture, new Rectangle(bar.X + 2 + filledWidth, bar.Y + 1, 4, 12), Color.White);
        }

        private static void DrawIngameClipboardButton(SpriteBatch spriteBatch, Rectangle bounds, string texturePath, bool hovered, string hoverText)
        {
            DrawResourcePackButtonBackground(spriteBatch, bounds, hovered);
            Texture2D texture = RequestTextureValue(texturePath);
            var destination = new Rectangle(bounds.Center.X - 7, bounds.Center.Y - 7, 14, 14);
            spriteBatch.Draw(texture, destination, Color.White);
            if (hovered) ShowHoverText(hoverText);
        }

        private static string GetColorPasteTooltip()
        {
            string clipboardText = TryGetClipboardText();
            return PluginColor.TryParseHex(clipboardText, out var color)
                ? "Paste color hex (" + color.ToHex() + ")"
                : "Paste color hex";
        }

        private static float NormalizeSlider(PluginSettingControl control) => MathHelper.Clamp((control.GetSlider() - control.Minimum) / (control.Maximum - control.Minimum), 0f, 1f);
        private static float DenormalizeSlider(float value, PluginSettingControl control)
        {
            float result = control.Minimum + MathHelper.Clamp(value, 0f, 1f) * (control.Maximum - control.Minimum);
            return control.Step <= 0f ? result : control.Minimum + (float)Math.Round((result - control.Minimum) / control.Step) * control.Step;
        }

        private static void SetIngameSliderValue(PluginSettingControl control, float normalizedValue)
        {
            try
            {
                float current = control.GetSlider();
                float next = DenormalizeSlider(normalizedValue, control);
                if (Math.Abs(current - next) <= 0.0001f)
                    return;

                control.SetSlider(next);
                // Discrete controls provide feedback at each real setting point. Continuous controls
                // intentionally stay quiet so a drag cannot become a stream of UI sounds.
                if (control.Step > 0f)
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            catch (Exception exception)
            {
                // A persistence failure is local to this setting; it must not escape the draw hook
                // and make the version-locked facade restore Terraria's General category.
                ShowHoverText("Unable to change plugin setting: " + exception.Message);
            }
        }

        private static bool HasSettings(PluginId pluginId) => _extensions.GetSettingsControls(pluginId).Count != 0 || _extensions.GetSettingsPages(pluginId).Any(page => page.IsInteractive);

        private static string ReadSettingValue(PluginSettingControl control)
        {
            try
            {
                switch (control.Kind)
                {
                    case PluginSettingControlKind.Toggle: return control.GetToggle() ? "Enabled" : "Disabled";
                    case PluginSettingControlKind.Cycle: return control.GetCycle();
                    case PluginSettingControlKind.Dropdown: return control.GetDropdown();
                    case PluginSettingControlKind.Slider:
                        float value = control.GetSlider();
                        return control.FormatSlider == null ? value.ToString("0.##") : control.FormatSlider(value);
                    case PluginSettingControlKind.Color: return control.GetColor().ToHex();
                    default: return "Unavailable";
                }
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read plugin setting", exception);
                return "Unavailable";
            }
        }

        private static void ActivateSetting(PluginSettingControl control)
        {
            try
            {
                if (control.Kind == PluginSettingControlKind.Toggle) control.SetToggle(!control.GetToggle());
                else if (control.Kind == PluginSettingControlKind.Cycle)
                {
                    var values = control.CycleValues;
                    int index = Array.IndexOf(values.ToArray(), control.GetCycle());
                    control.SetCycle(values[(index + 1 + values.Count) % values.Count]);
                }
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            catch (Exception exception) { ShowHoverText("Unable to change plugin setting: " + exception.Message); }
        }

        private static string ReadSettingValue(PluginUiContribution contribution)
        {
            try { return contribution.ValueText == null ? string.Empty : contribution.ValueText(); }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read legacy plugin setting", exception);
                return "Unavailable";
            }
        }

        private static void ActivateSetting(PluginUiContribution contribution)
        {
            try
            {
                contribution.Activate?.Invoke();
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            catch (Exception exception)
            {
                ShowHoverText("Unable to change plugin setting: " + exception.Message);
            }
        }

        // Mirrors the character creator's ReLogic clipboard contract without creating a hard compile-time dependency.
        private static string TryGetClipboardText()
        {
            try
            {
                Type platform = Type.GetType("ReLogic.OS.Platform, ReLogic", false);
                Type clipboard = Type.GetType("ReLogic.OS.IClipboard, ReLogic", false);
                MethodInfo get = platform == null || clipboard == null ? null : platform.GetMethod("Get", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(clipboard);
                object service = get == null ? null : get.Invoke(null, null);
                PropertyInfo value = service == null ? null : service.GetType().GetProperty("Value");
                return value == null ? string.Empty : value.GetValue(service, null) as string ?? string.Empty;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Read clipboard", exception);
                return string.Empty;
            }
        }

        private static void TrySetClipboardText(string text)
        {
            try
            {
                Type platform = Type.GetType("ReLogic.OS.Platform, ReLogic", false);
                Type clipboard = Type.GetType("ReLogic.OS.IClipboard, ReLogic", false);
                MethodInfo get = platform == null || clipboard == null ? null : platform.GetMethod("Get", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(clipboard);
                object service = get == null ? null : get.Invoke(null, null);
                service?.GetType().GetProperty("Value")?.SetValue(service, text, null);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Write clipboard", exception);
            }
        }

        private static void DrawIngameActionButton(SpriteBatch spriteBatch, Rectangle bounds, string text, bool hovered)
        {
            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, hovered ? ResourcePackHoverBackground : ResourcePackBorder);
            Utils.DrawBorderString(spriteBatch, text, new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.6f, 0.5f, 0.5f, -1);
        }

        private static void DrawIngameIconButton(SpriteBatch spriteBatch, Rectangle bounds, string texturePath, bool hovered, string hoverText)
        {
            DrawResourcePackButtonBackground(spriteBatch, bounds, hovered);
            try
            {
                Texture2D texture = RequestTextureValue(texturePath);
                const int iconSize = 14;
                var destination = new Rectangle(bounds.Center.X - iconSize / 2, bounds.Center.Y - iconSize / 2, iconSize, iconSize);
                spriteBatch.Draw(texture, destination, Color.White);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("Draw in-game icon", exception);
                Utils.DrawBorderString(spriteBatch, "?", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.7f, 0.5f, 0.5f, -1);
            }

            if (hovered)
                ShowHoverText(hoverText);
        }

        private static void DrawResourcePackButtonBackground(SpriteBatch spriteBatch, Rectangle bounds, bool hovered)
        {
            Texture2D panel = RequestTextureValue("Images/UI/CharCreation/PanelGrayscale");
            Color panelColor = Color.Lerp(Color.Black, Color.White, 0.8f) * 0.5f;
            Utils.DrawSplicedPanel(spriteBatch, panel, bounds.X, bounds.Y, bounds.Width, bounds.Height, 10, 10, 10, 10, panelColor);
            if (hovered)
            {
                Texture2D border = RequestTextureValue("Images/UI/CharCreation/CategoryPanelBorder");
                Utils.DrawSplicedPanel(spriteBatch, border, bounds.X, bounds.Y, bounds.Width, bounds.Height, 10, 10, 10, 10, Color.White);
            }
        }

        private static Texture2D RequestTextureValue(string path)
        {
            object assets = GetMainAssets();
            if (_assetRequest == null)
            {
                _assetRequest = assets.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => {
                        ParameterInfo[] parameters = method.GetParameters();
                        return method.Name == "Request" && method.IsGenericMethodDefinition && parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType.IsEnum;
                    });
            }

            if (_assetRequest == null)
                throw new MissingMethodException(assets.GetType().FullName, "Request<T>(string, AssetRequestMode)");

            Type modeType = _assetRequest.GetParameters()[1].ParameterType;
            object asset = _assetRequest.MakeGenericMethod(typeof(Texture2D)).Invoke(assets, new object[] { path, Enum.ToObject(modeType, 1) });
            _assetValue = _assetValue ?? asset.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return (Texture2D)_assetValue.GetValue(asset, null);
        }

        internal static Texture2D RequestApprovedTexture(string path) => RequestTextureValue(path);

        /// <summary>Resolves a scoped icon interaction for a Terraria-owned immediate-mode surface.</summary>
        internal static PluginIconInteractionState EvaluateIconInteraction(PluginId owner, string id, PluginUiRect bounds)
        {
            PluginExtensionHost extensions = _extensions;
            return extensions == null ? default(PluginIconInteractionState) : extensions.EvaluateIconInteraction(owner, id, bounds, Main.mouseX, Main.mouseY);
        }

        /// <summary>Invokes one hovered scoped icon action for the current primary-pointer press.</summary>
        internal static bool TryActivateIconInteraction(PluginId owner, string id, PluginUiRect bounds)
        {
            PluginExtensionHost extensions = _extensions;
            if (extensions == null || _iconInteractionConsumed || !_iconInteractionPressed || !bounds.Contains(Main.mouseX, Main.mouseY))
                return false;
            bool activated = extensions.TryActivateIconInteraction(owner, id);
            if (activated)
            {
                _iconInteractionConsumed = true;
                Main.mouseLeftRelease = false;
            }
            return activated;
        }

        private static void UpdateIconInteractionInput()
        {
            uint tick = Main.GameUpdateCount;
            if (tick == _iconInteractionInputTick)
                return;
            bool down = Main.mouseLeft;
            _iconInteractionPressed = down && !_iconInteractionWasDown;
            _iconInteractionWasDown = down;
            _iconInteractionConsumed = false;
            _iconInteractionInputTick = tick;
        }

        /// <summary>Draws a host-owned tooltip without exposing Terraria rendering types through the SDK.</summary>
        internal static void DrawIconTooltip(SpriteBatch spriteBatch, PluginIconInteractionState state)
        {
            if (spriteBatch == null || !state.IsHovered || state.Tooltip == null)
                return;
            PluginTooltipOptions tooltip = state.Tooltip;
            Vector2 position = new Vector2(Main.mouseX, Main.mouseY);
            float originX = 0f;
            float originY = 0f;
            switch (tooltip.Placement)
            {
                case PluginTooltipPlacement.Left:
                    position.X -= 16f; originX = 1f; originY = 0.5f; break;
                case PluginTooltipPlacement.Right:
                    position.X += 16f; originY = 0.5f; break;
                case PluginTooltipPlacement.Above:
                    position.Y -= 16f; originX = 0.5f; originY = 1f; break;
                case PluginTooltipPlacement.Below:
                    position.Y += 16f; originX = 0.5f; break;
                default:
                    position.X += 16f; position.Y += 16f; break;
            }
            PluginColor color = tooltip.Color ?? new PluginColor(255, 255, 255);
            Utils.DrawBorderString(spriteBatch, tooltip.Text, position, new Color(color.Red, color.Green, color.Blue), tooltip.Scale, originX, originY, -1);
        }

        private static object GetMainAssets()
        {
            _mainAssetsField = _mainAssetsField ?? typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (_mainAssetsField == null)
                throw new MissingFieldException(typeof(Main).FullName, "Assets");

            object owner = _mainAssetsField.IsStatic ? null : Main.instance;
            object assets = _mainAssetsField.GetValue(owner);
            if (assets == null)
                throw new InvalidOperationException("Terraria Main.Assets is unavailable.");
            return assets;
        }

        private static void ReportOptionalUiFailure(string operation, Exception exception)
        {
            string key = operation + ": " + exception.GetType().FullName + ": " + exception.Message;
            if (ReportedOptionalUiFailures.Add(key))
                Debug.WriteLine("Alacrity optional UI feature failed: " + key);
        }

        private static void UpdateIngameScroll(Rectangle bounds, int contentHeight, int visibleHeight, int rowStep)
        {
            if (bounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta != 0)
                    _ingameScroll -= Math.Sign(delta) * rowStep;
            }

            _ingameScroll = Math.Max(0f, Math.Min(_ingameScroll, Math.Max(0, contentHeight - visibleHeight)));
        }

        private static void UpdateIngameDescriptionScroll(Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (bounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta != 0) _ingameDescriptionScroll -= Math.Sign(delta) * 30f;
            }
            _ingameDescriptionScroll = Math.Max(0f, Math.Min(_ingameDescriptionScroll, Math.Max(0, contentHeight - visibleHeight)));
        }

        private static void DrawIngameScrollbar(SpriteBatch spriteBatch, Rectangle bounds, int contentHeight, int visibleHeight)
        {
            float maxScroll = Math.Max(0f, contentHeight - visibleHeight);
            if (maxScroll <= 0f)
                return;

            EnsureIngameBlankTexture(spriteBatch);
            if (_ingameBlankTexture == null)
                return;

            // Match the list's 16px top/bottom content insets. The old full-pane track
            // protruded beyond the actual scrollable area at the bottom.
            int trackX = bounds.Right - 12;
            int trackY = bounds.Top + 16;
            int trackHeight = visibleHeight;
            var track = new Rectangle(trackX, trackY, 4, trackHeight);
            spriteBatch.Draw(_ingameBlankTexture, track, new Color(18, 12, 58, 180));

            int thumbHeight = Math.Max(28, (int)(trackHeight * Math.Min(1f, visibleHeight / (float)contentHeight)));
            int thumbY = trackY + (int)((trackHeight - thumbHeight) * (_ingameScroll / maxScroll));
            var thumb = new Rectangle(trackX - 1, thumbY, 6, thumbHeight);
            spriteBatch.Draw(_ingameBlankTexture, thumb, new Color(180, 170, 255, 220));
            UpdateIngamePluginListScrollbarCapture(track, thumb, thumbHeight, maxScroll);
        }

        private static void DrawIngameDescriptionScrollbar(SpriteBatch spriteBatch, Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (contentHeight <= visibleHeight) return;
            int trackX = bounds.Right - 13;
            int trackY = bounds.Y + 82;
            int trackHeight = visibleHeight;
            int thumbHeight = Math.Max(16, (int)(trackHeight * (visibleHeight / (float)contentHeight)));
            float maxScroll = contentHeight - visibleHeight;
            int thumbY = trackY + (int)((trackHeight - thumbHeight) * (_ingameDescriptionScroll / maxScroll));
            Utils.DrawInvBG(spriteBatch, trackX, trackY, 5, trackHeight, ResourcePackBorder);
            var track = new Rectangle(trackX, trackY, 5, trackHeight);
            var thumb = new Rectangle(trackX, thumbY, 5, thumbHeight);
            Utils.DrawInvBG(spriteBatch, thumb.X, thumb.Y, thumb.Width, thumb.Height, ResourcePackHoverBackground);
            UpdateIngameDescriptionScrollbarCapture(track, thumb, thumbHeight, maxScroll);
        }

        private static bool HasIngamePointerCapture => !string.IsNullOrEmpty(_ingamePointerCaptureId);

        private static bool IsIngamePointerCaptured(string id)
        {
            return string.Equals(_ingamePointerCaptureId, id, StringComparison.Ordinal);
        }

        private static void UpdateIngamePointerCapture()
        {
            if (!Main.mouseLeft)
            {
                _ingamePointerCaptureId = null;
                return;
            }

            if (HasIngamePointerCapture)
                ConsumeIngamePointer();
        }

        private static void BeginIngamePointerCapture(string id)
        {
            if (HasIngamePointerCapture || string.IsNullOrEmpty(id))
                return;

            _ingamePointerCaptureId = id;
            _ingameHoveredSettingId = id;
            ConsumeIngamePointer();
        }

        private static void ConsumeIngamePointer()
        {
            Main.mouseLeftRelease = false;
            MarkIngameMouseInterface();
        }

        private static void MarkIngameMouseInterface()
        {
            if (Main.myPlayer >= 0 && Main.myPlayer < Main.player.Length && Main.player[Main.myPlayer] != null)
                Main.player[Main.myPlayer].mouseInterface = true;
        }

        private static void UpdateIngamePluginListScrollbarCapture(Rectangle track, Rectangle thumb, int thumbHeight, float maxScroll)
        {
            const string captureId = "plugin-list-scrollbar";
            bool captured = IsIngamePointerCaptured(captureId);
            if (!captured && !HasIngamePointerCapture && Main.mouseLeft && thumb.Contains(Main.mouseX, Main.mouseY))
            {
                BeginIngamePointerCapture(captureId);
                captured = true;
            }

            if (!captured || !Main.mouseLeft)
                return;

            int travel = track.Height - thumbHeight;
            float fraction = travel <= 0
                ? 0f
                : MathHelper.Clamp((Main.mouseY - track.Y - thumbHeight / 2f) / travel, 0f, 1f);
            _ingameScroll = fraction * maxScroll;
            ConsumeIngamePointer();
        }

        private static void UpdateIngameDescriptionScrollbarCapture(Rectangle track, Rectangle thumb, int thumbHeight, float maxScroll)
        {
            const string captureId = "plugin-description-scrollbar";
            bool captured = IsIngamePointerCaptured(captureId);
            if (!captured && !HasIngamePointerCapture && Main.mouseLeft && thumb.Contains(Main.mouseX, Main.mouseY))
            {
                BeginIngamePointerCapture(captureId);
                captured = true;
            }

            if (!captured || !Main.mouseLeft)
                return;

            int travel = track.Height - thumbHeight;
            float fraction = travel <= 0
                ? 0f
                : MathHelper.Clamp((Main.mouseY - track.Y - thumbHeight / 2f) / travel, 0f, 1f);
            _ingameDescriptionScroll = fraction * maxScroll;
            ConsumeIngamePointer();
        }

        private static void EnsureIngameBlankTexture(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null)
                return;
            if (_ingameBlankTexture != null && !_ingameBlankTexture.IsDisposed && ReferenceEquals(_ingameBlankTextureDevice, spriteBatch.GraphicsDevice)) return;

            try
            {
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1);
                _ingameBlankTexture.SetData(new[] { Color.White });
                _ingameBlankTextureDevice = spriteBatch.GraphicsDevice;
            }
            catch
            {
                _ingameBlankTexture = null;
                _ingameBlankTextureDevice = null;
            }
        }

        /// <summary>Returns the integration-owned reusable pixel texture for native Alacrity UI surfaces.</summary>
        internal static Texture2D GetIngameBlankTexture(SpriteBatch spriteBatch)
        {
            EnsureIngameBlankTexture(spriteBatch);
            return _ingameBlankTexture;
        }


    }
}
