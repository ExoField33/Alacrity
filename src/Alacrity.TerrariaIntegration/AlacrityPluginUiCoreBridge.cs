using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Alacrity.App;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria.UI.Gamepad;
using Terraria.Utilities;

namespace AlacrityTerraria
{
    public static class PluginUiRuntime
    {
        private static PluginManagerRuntime _runtime;
        private static PluginManagementMenu _menu;
        private static PluginNotificationCenter _notifications;
        private static PluginDependencyDiagnostics _diagnostics;
        private static PluginExtensionHost _extensions;
        private static PluginChatHost _chat;
        private static readonly PluginManagerPresenter _presenter = new PluginManagerPresenter();
        private static readonly Color ResourcePackBackground = new Color(26, 40, 89) * 0.8f;
        private static readonly Color ResourcePackBorder = new Color(13, 20, 44) * 0.8f;
        private static readonly Color ResourcePackHoverBackground = new Color(46, 60, 119);
        private static readonly Color ResourcePackHoverBorder = new Color(20, 30, 56);
        private static MethodInfo _assetRequest;
        private static MethodInfo _assetFrame;
        private static PropertyInfo _assetValue;
        private static FieldInfo _mainAssetsField;
        private static readonly HashSet<string> ReportedOptionalUiFailures = new HashSet<string>(StringComparer.Ordinal);
        private static Texture2D _ingameBlankTexture;
        private static bool _pluginMenuOpen;
        private static PluginSelectionMenu _selectionMenu;
        private static PluginManagerRow[] _ingameEntries = Array.Empty<PluginManagerRow>();
        private static int _ingameSelectedEntry;
        private static int _ingameView;
        private static float _ingameScroll;
        private static float _ingameDescriptionScroll;
        private static string _ingameHoveredSettingId;
        private static bool _enabledStateRestored;
        private static bool _chatCatalogInitialized;

        /// <summary>Returns whether an enabled plugin owns a chat editor. The injected hook calls this only while player chat is focused.</summary>
        public static bool IsBetterChatActive()
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasInputEditors;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat activation", exception);
                return false;
            }
        }

        /// <summary>Processes player-chat input through the enabled scoped chat editor registrations.</summary>
        public static string ProcessPlayerChatInput(string text, bool allowMultiLine)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasInputEditors ? BetterChatRuntime.Process(_chat, text, allowMultiLine) : text;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat input", exception);
                return text;
            }
        }

        /// <summary>Creates draw-only chat markup. It never modifies Main.chatText or outgoing packet text.</summary>
        public static string FormatPlayerChatText(string text)
        {
            try { return BetterChatRuntime.FormatForDraw(IsBetterChatActive(), text); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat draw text", exception); return text; }
        }

        /// <summary>Decorates parsed normal chat snippets outside the draw loop.</summary>
        public static object DecorateChatMessage(object snippets, Color baseColor, string originalMessage)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasMessageDecorators ? BetterChatRuntime.Decorate(_chat, snippets, baseColor, originalMessage) : snippets;
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat message decoration", exception);
                return snippets;
            }
        }

        /// <summary>Filters network chat before Terraria creates overhead or scrolling-chat entries.</summary>
        public static bool ShouldDisplayNetworkChatMessage(byte messageAuthor)
        {
            try
            {
                EnsureChatRuntime();
                if (_chat == null || !_chat.HasMessageFilters) return true;
                return _chat.ShouldDisplay(messageAuthor == byte.MaxValue ? ChatMessageOrigin.Server : ChatMessageOrigin.Player);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat network visibility", exception);
                return true;
            }
        }

        /// <summary>Filters client-originated system messages without affecting network receive behavior.</summary>
        public static bool ShouldDisplayLocalChatMessage()
        {
            try
            {
                EnsureChatRuntime();
                return _chat == null || !_chat.HasMessageFilters || _chat.ShouldDisplay(ChatMessageOrigin.LocalSystem);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat local visibility", exception);
                return true;
            }
        }

        /// <summary>Shows bounded vanilla-style hover feedback and handles copy-on-right-click.</summary>
        public static void HandleChatSnippetHover(object snippet)
        {
            try { if (IsBetterChatActive()) BetterChatRuntime.Hover(snippet); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat hover", exception); }
        }

        /// <summary>Activates only validated http or https links registered by an enabled plugin.</summary>
        public static bool HandleChatSnippetClick(object snippet)
        {
            try
            {
                EnsureChatRuntime();
                return _chat != null && _chat.HasLinkHandlers && BetterChatRuntime.Click(_chat, snippet);
            }
            catch (Exception exception)
            {
                ReportOptionalUiFailure("BetterChat link activation", exception);
                return false;
            }
        }

        /// <summary>Applies the current hover highlight without mutating the original snippet color.</summary>
        public static Color GetChatSnippetVisibleColor(object snippet, Color color)
        {
            try { return IsBetterChatActive() ? BetterChatRuntime.VisibleColor(snippet, color) : color; }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat hover color", exception); return color; }
        }

        /// <summary>Transfers parse-time line ownership when Terraria clones a snippet during layout.</summary>
        public static void CopyChatSnippetContext(object source, object copy)
        {
            try { BetterChatRuntime.CopyContext(source, copy); }
            catch (Exception exception) { ReportOptionalUiFailure("BetterChat snippet copy", exception); }
        }

        public static void Open()
        {
            EnsurePluginManager();
            RefreshPluginCatalog();
            Main.menuMode = 888;
            _selectionMenu = new PluginSelectionMenu(_menu);
            Main.MenuUI.SetState(_selectionMenu);
            _pluginMenuOpen = true;
        }

        public static bool HandlePluginMenuInput()
        {
            if (!_pluginMenuOpen || Main.menuMode != 888 || !Main.keyState.IsKeyDown(Keys.Escape) || Main.oldKeyState.IsKeyDown(Keys.Escape))
                return true;

            if (_selectionMenu != null && Main.MenuUI.CurrentState is PluginDescriptionMenu)
                ReturnToPluginList();
            else
                Close();
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
        }

        public static void DrawIngamePluginSettings(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null)
                return;

            // Terraria centers General's right-column controls at screen center + 167.5.
            // Anchor this immediate-mode panel to that same verified column center.
            var bounds = new Rectangle(Main.screenWidth / 2 + 10, Main.screenHeight / 2 - 184, 315, 418);
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
        public static void DrawNotifications(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || _notifications == null)
                return;

            int y = 96;
            foreach (var notification in _notifications.GetActive(DateTimeOffset.UtcNow))
            {
                Utils.DrawBorderString(spriteBatch, notification.Message, new Vector2(Main.screenWidth - 18, y), Color.LightGoldenrodYellow, 0.72f, 1f, 0f, -1);
                y += 24;
            }
        }

        private static void OpenDescription(PluginManagerRow plugin)
        {
            Main.MenuUI.SetState(new PluginDescriptionMenu(plugin));
        }

        private static void DrawIngamePluginList(SpriteBatch spriteBatch, Rectangle bounds)
        {
            const int rowHeight = 70;
            const int rowSpacing = 6;
            int contentTop = bounds.Y + 16;
            int contentHeight = _ingameEntries.Length * (rowHeight + rowSpacing) - rowSpacing;
            int visibleHeight = bounds.Height - 32;
            UpdateIngameScroll(bounds, contentHeight, visibleHeight);

            for (int index = 0; index < _ingameEntries.Length; index++)
            {
                int rowY = contentTop + index * (rowHeight + rowSpacing) - (int)_ingameScroll;
                const int rowWidth = 264;
                var row = new Rectangle(bounds.Center.X - rowWidth / 2, rowY, rowWidth, rowHeight);
                if (row.Bottom < contentTop || row.Top > bounds.Bottom - 16)
                    continue;

                DrawIngamePluginRow(spriteBatch, row, _ingameEntries[index]);
            }

            DrawIngameScrollbar(spriteBatch, bounds, contentHeight, visibleHeight);
        }

        private static void DrawIngamePluginRow(SpriteBatch spriteBatch, Rectangle row, PluginManagerRow plugin)
        {
            bool rowHovered = row.Contains(Main.mouseX, Main.mouseY);
            Utils.DrawInvBG(spriteBatch, row.X, row.Y, row.Width, row.Height, rowHovered ? ResourcePackHoverBackground : ResourcePackBackground);
            Utils.DrawBorderString(spriteBatch, plugin.Name, new Vector2(row.Center.X, row.Y + 9), Color.White, 0.8f, 0.5f, 0f, -1);
            if (!string.IsNullOrWhiteSpace(plugin.Warning))
                Utils.DrawBorderString(spriteBatch, plugin.Warning, new Vector2(row.Center.X, row.Y + 27), Color.Orange, 0.55f, 0.5f, 0f, -1);

            const int buttonHeight = 21;
            const int buttonGap = 6;
            int buttonWidth = (int)(((row.Width - buttonGap) / 2f) * 0.8f);
            int buttonsWidth = buttonWidth * 2 + buttonGap;
            int buttonY = string.IsNullOrWhiteSpace(plugin.Warning) ? row.Y + 40 : row.Y + 44;
            int buttonX = row.Center.X - buttonsWidth / 2;
            var description = new Rectangle(buttonX, buttonY, buttonWidth, buttonHeight);
            var settings = new Rectangle(buttonX + buttonWidth + buttonGap, buttonY, buttonWidth, buttonHeight);
            bool descriptionHovered = description.Contains(Main.mouseX, Main.mouseY);
            bool settingsHovered = settings.Contains(Main.mouseX, Main.mouseY);
            DrawIngameIconButton(spriteBatch, description, "Images/UI/CharCreation/CharInfo", descriptionHovered, "Plugin Description");
            DrawIngameIconButton(spriteBatch, settings, "Images/UI/Camera_1", settingsHovered, "Plugin Settings");

            if (!Main.mouseLeft || !Main.mouseLeftRelease)
                return;

            int index = Array.IndexOf(_ingameEntries, plugin);
            if (descriptionHovered)
            {
                _ingameSelectedEntry = index;
                _ingameView = 1;
                _ingameDescriptionScroll = 0f;
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            }
            else if (settingsHovered)
            {
                if (!HasSettings(plugin.Id))
                    Main.instance.MouseText("No plugin settings are available.");
                else
                {
                    _ingameSelectedEntry = index;
                    _ingameView = 2;
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                }
            }
        }

        private static void DrawIngamePluginDescription(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name, new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            Utils.DrawBorderString(spriteBatch, "Version: " + plugin.Version, new Vector2(bounds.Center.X, bounds.Y + 50), Color.White, 0.7f, 0.5f, 0f, -1);
            string body = "Description\n" + (string.IsNullOrWhiteSpace(plugin.Description) ? "No information provided." : plugin.Description) + "\n\nChangelog\n" + (string.IsNullOrWhiteSpace(plugin.Changelog) ? "No information provided." : plugin.Changelog);
            string[] lines = WrapPluginText(body, 44);
            const int top = 82;
            const int lineHeight = 17;
            int visibleHeight = bounds.Height - top - 18;
            int contentHeight = lines.Length * lineHeight;
            UpdateIngameDescriptionScroll(bounds, contentHeight, visibleHeight);
            int firstLine = (int)(_ingameDescriptionScroll / lineHeight);
            int lastLine = Math.Min(lines.Length, firstLine + visibleHeight / lineHeight + 2);
            for (int index = firstLine; index < lastLine; index++)
            {
                bool heading = lines[index] == "Description" || lines[index] == "Changelog";
                Utils.DrawBorderString(spriteBatch, lines[index], new Vector2(bounds.X + 18, bounds.Y + top + index * lineHeight - _ingameDescriptionScroll), heading ? Color.White : Color.White, heading ? 0.8f : 0.65f, 0f, 0f, -1);
            }
            DrawIngameDescriptionScrollbar(spriteBatch, bounds, contentHeight, visibleHeight);
        }

        private static string[] WrapPluginText(string text, int maximumCharacters)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length > maximumCharacters)
                    {
                        if (current.Length != 0) { lines.Add(current); current = string.Empty; }
                        for (int offset = 0; offset < word.Length; offset += maximumCharacters)
                            lines.Add(word.Substring(offset, Math.Min(maximumCharacters, word.Length - offset)));
                        continue;
                    }
                    if (current.Length != 0 && current.Length + word.Length + 1 > maximumCharacters)
                    {
                        lines.Add(current);
                        current = word;
                    }
                    else current = current.Length == 0 ? word : current + " " + word;
                }
                lines.Add(current);
            }
            return lines.ToArray();
        }

        private static void DrawIngamePluginSettingsPage(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
        {
            Utils.DrawBorderString(spriteBatch, plugin.Name + " Settings", new Vector2(bounds.Center.X, bounds.Y + 16), Color.White, 0.9f, 0.5f, 0f, -1);
            var controls = _extensions.GetSettingsControls(plugin.Id);
            var pages = _extensions.GetSettingsPages(plugin.Id).Where(page => page.IsInteractive).ToArray();
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
                bool hovered = hitArea.Contains(Main.mouseX, Main.mouseY);
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
        }

        private static int DrawIngameTypedControl(SpriteBatch spriteBatch, Rectangle bounds, int y, PluginSettingControl control, ref bool anyHovered)
        {
            if (control.Kind == PluginSettingControlKind.Color)
            {
                var swatch = new Rectangle(bounds.X + 18, y - 7, 20, 20);
                Utils.DrawInvBG(spriteBatch, swatch.X, swatch.Y, swatch.Width, swatch.Height, new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue));
                Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + control.GetColor().ToHex(), new Vector2(bounds.X + 46, y), Color.White, 0.7f, 0f, 0f, -1);
                var copy = new Rectangle(bounds.Right - 73, y - 8, 25, 22);
                var paste = new Rectangle(bounds.Right - 42, y - 8, 25, 22);
                bool copyHover = copy.Contains(Main.mouseX, Main.mouseY), pasteHover = paste.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= copyHover || pasteHover;
                DrawIngameClipboardButton(spriteBatch, copy, "Images/UI/CharCreation/Copy", copyHover, "Copy color hex");
                DrawIngameClipboardButton(spriteBatch, paste, "Images/UI/CharCreation/Paste", pasteHover, "Paste color hex");
                if (Main.mouseLeft && Main.mouseLeftRelease && copyHover) TrySetClipboardText(control.GetColor().ToHex());
                if (Main.mouseLeft && Main.mouseLeftRelease && pasteHover && PluginColor.TryParseHex(TryGetClipboardText(), out var pasted)) control.SetColor(pasted);
                return 34;
            }
            if (control.Kind == PluginSettingControlKind.Slider)
            {
                var bar = new Rectangle(bounds.Right - 150, y - 5, 132, 14);
                bool hovered = bar.Contains(Main.mouseX, Main.mouseY);
                anyHovered |= hovered;
                DrawIngameSlider(spriteBatch, bar, NormalizeSlider(control));
                Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.X + 18, y), Color.White, 0.7f, 0f, 0f, -1);
                if (hovered && Main.mouseLeft) control.SetSlider(DenormalizeSlider((Main.mouseX - bar.X) / (float)bar.Width, control));
                return 32;
            }
            var hitArea = new Rectangle(bounds.X + 18, y - 9, bounds.Width - 36, 26);
            bool hover = hitArea.Contains(Main.mouseX, Main.mouseY);
            anyHovered |= hover;
            if (hover && _ingameHoveredSettingId != control.Id) SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
            if (hover) _ingameHoveredSettingId = control.Id;
            Utils.DrawBorderString(spriteBatch, control.DisplayName + ": " + ReadSettingValue(control), new Vector2(bounds.Center.X, y), hover ? new Color(255, 230, 140) : Color.White, hover ? 0.8f : 0.7f, 0.5f, 0f, -1);
            if (hover && Main.mouseLeft && Main.mouseLeftRelease) ActivateSetting(control);
            return 30;
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
            if (hovered) Main.instance.MouseText(hoverText);
        }

        private static float NormalizeSlider(PluginSettingControl control) => MathHelper.Clamp((control.GetSlider() - control.Minimum) / (control.Maximum - control.Minimum), 0f, 1f);
        private static float DenormalizeSlider(float value, PluginSettingControl control)
        {
            float result = control.Minimum + MathHelper.Clamp(value, 0f, 1f) * (control.Maximum - control.Minimum);
            return control.Step <= 0f ? result : control.Minimum + (float)Math.Round((result - control.Minimum) / control.Step) * control.Step;
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
            catch (Exception exception) { Main.instance.MouseText("Unable to change plugin setting: " + exception.Message); }
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
                Main.instance.MouseText("Unable to change plugin setting: " + exception.Message);
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
                Main.instance.MouseText(hoverText);
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

        private static object GetMainAssets()
        {
            _mainAssetsField = _mainAssetsField ?? typeof(Main).GetField("Assets", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (_mainAssetsField == null)
                throw new MissingFieldException(typeof(Main).FullName, "Assets");

            object assets = _mainAssetsField.GetValue(null);
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

        private static void UpdateIngameScroll(Rectangle bounds, int contentHeight, int visibleHeight)
        {
            if (bounds.Contains(Main.mouseX, Main.mouseY))
            {
                int delta = Terraria.GameInput.PlayerInput.ScrollWheelDelta;
                if (delta != 0)
                    _ingameScroll -= Math.Sign(delta) * 30f;
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

            int trackX = bounds.Right - 12;
            int trackY = bounds.Top;
            var track = new Rectangle(trackX, trackY, 4, bounds.Height);
            spriteBatch.Draw(_ingameBlankTexture, track, new Color(18, 12, 58, 180));

            int thumbHeight = Math.Max(28, (int)(bounds.Height * Math.Min(1f, visibleHeight / (float)contentHeight)));
            int thumbY = trackY + (int)((bounds.Height - thumbHeight) * (_ingameScroll / maxScroll));
            var thumb = new Rectangle(trackX - 1, thumbY, 6, thumbHeight);
            spriteBatch.Draw(_ingameBlankTexture, thumb, new Color(180, 170, 255, 220));
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
            Utils.DrawInvBG(spriteBatch, trackX, thumbY, 5, thumbHeight, ResourcePackHoverBackground);
        }

        private static void EnsureIngameBlankTexture(SpriteBatch spriteBatch)
        {
            if (spriteBatch == null || (_ingameBlankTexture != null && !_ingameBlankTexture.IsDisposed))
                return;

            try
            {
                _ingameBlankTexture?.Dispose();
                _ingameBlankTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1);
                _ingameBlankTexture.SetData(new[] { Color.White });
            }
            catch
            {
                _ingameBlankTexture = null;
            }
        }

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

        private static void EnsurePluginManager()
        {
            if (_menu != null)
                return;

            string root = AppDomain.CurrentDomain.BaseDirectory;
            _notifications = new PluginNotificationCenter();
            _diagnostics = new PluginDependencyDiagnostics();
            string patchDirectory = Path.Combine(root, "data", "patches");
            Directory.CreateDirectory(patchDirectory);
            var patchHost = PatchHost.CreateManaged(root, Path.Combine(patchDirectory, "journal.json"));
            _extensions = new PluginExtensionHost();
            _chat = new PluginChatHost();
            var contexts = new PluginHostContextFactory(root, new PluginServiceHub(), _extensions, new PluginCommandHost(), null, _chat);
            var runtimeHost = new PluginRuntimeHost(new PluginPackageCatalog(new PluginPackageManifestReader()), new PluginAssemblyLoader(), contexts);
            var activation = new PluginActivationCoordinator(patchHost, new PluginEnablePlanner(), new PluginEnableExecutor(_notifications), new PluginActivationGate(_diagnostics));
            _runtime = new PluginManagerRuntime(runtimeHost, new PluginPackageLifecycleRegistry(), activation);
            _menu = new PluginManagementMenu(_runtime);
        }

        private static void RefreshPluginCatalog()
        {
            _runtime.Discover(AppDomain.CurrentDomain.BaseDirectory);
            foreach (var record in _runtime.Registry.Records)
            {
                if (record.State != PluginPackageLifecycleState.Discovered || record.Manifest.EntryAssembly == null || record.Manifest.EntryType == null)
                    continue;

                try
                {
                    // Cryptographic release verification is intentionally staged. Until then,
                    // only packages present in the user's local package directory are loaded
                    // under an explicit local-development trust decision.
                    _runtime.LoadTrusted(record.Manifest.Id,
                        new PluginTrustVerificationResult(PluginTrustLevel.LocallyTrusted, "Locally installed package; cryptographic verification is not configured."),
                        new BridgePluginLogger(record.Manifest.Id),
                        new TerrariaMultiplayerSession());
                }
                catch (Exception exception)
                {
                    _runtime.Registry.MarkFaulted(record.Manifest.Id, exception.Message);
                }
            }
            RestoreEnabledPlugins();
        }

        private static void EnsureChatRuntime()
        {
            EnsurePluginManager();
            if (_chatCatalogInitialized)
                return;
            _chatCatalogInitialized = true;
            RefreshPluginCatalog();
        }

        private static string PluginStatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "plugin-state.json");
        private static string LegacyEnabledPluginsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "enabled-plugins.txt");

        private static void RestoreEnabledPlugins()
        {
            if (_enabledStateRestored) return;
            _enabledStateRestored = true;
            try
            {
                var requested = ReadEnabledPluginIds();
                foreach (var record in _runtime.Registry.Records.Where(record => requested.Contains(record.Manifest.Id.Value) && (record.State == PluginPackageLifecycleState.Loaded || record.State == PluginPackageLifecycleState.Disabled)).ToArray())
                    _runtime.Enable(record.Manifest.Id);
                if (!File.Exists(PluginStatePath) && File.Exists(LegacyEnabledPluginsPath))
                    PersistEnabledPlugins();
            }
            catch (Exception exception)
            {
                _notifications.Publish("Unable to restore enabled plugins: " + exception.Message, TimeSpan.FromSeconds(4));
            }
        }

        private static void PersistEnabledPlugins()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PluginStatePath));
                var records = _runtime.Registry.Records.Where(record => record.State != PluginPackageLifecycleState.Uninstalled).OrderBy(record => record.Manifest.Id.Value, StringComparer.Ordinal).ToArray();
                string json = "{\n  \"plugins\": [\n" + string.Join(",\n", records.Select(record => "    { \"id\": \"" + record.Manifest.Id.Value + "\", \"enabled\": " + (record.State == PluginPackageLifecycleState.Enabled ? "true" : "false") + " }")) + "\n  ]\n}\n";
                string temporaryPath = PluginStatePath + ".tmp";
                File.WriteAllText(temporaryPath, json);
                File.Copy(temporaryPath, PluginStatePath, true);
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                _notifications.Publish("Unable to save enabled plugins: " + exception.Message, TimeSpan.FromSeconds(4));
            }
        }

        private static HashSet<string> ReadEnabledPluginIds()
        {
            if (File.Exists(PluginStatePath))
            {
                string json = File.ReadAllText(PluginStatePath);
                return new HashSet<string>(Regex.Matches(json, "\\\"id\\\"\\s*:\\s*\\\"([a-z0-9.-]+)\\\"\\s*,\\s*\\\"enabled\\\"\\s*:\\s*true", RegexOptions.CultureInvariant).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
            }
            return File.Exists(LegacyEnabledPluginsPath) ? new HashSet<string>(File.ReadAllLines(LegacyEnabledPluginsPath), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class PluginSelectionMenu : UIState
        {
            private readonly UIList _availableList = new UIList();
            private readonly UIList _enabledList = new UIList();
            private readonly UIList _packageList = new UIList();
            private readonly UIText _availableTitle = new UIText("", 1f, false);
            private readonly UIText _enabledTitle = new UIText("", 1f, false);
            private readonly UIText _packageTitle = new UIText("", 1f, false);
            private UIGamepadHelper _gamepadHelper;
            private UIText _settingsHint;
            private DateTime _nextStatusRefreshUtc;
            private DateTime _manualHintExpiresUtc;
            private bool _addRemoveView;

            public PluginSelectionMenu(PluginManagementMenu menu)
            {
                BuildPage();
            }

            private void BuildPage()
            {
                RemoveAllChildren();
                var root = new UIElement {
                    Width = new StyleDimension(0f, 0.8f),
                    MaxWidth = new StyleDimension(800f, 0f),
                    MinWidth = new StyleDimension(600f, 0f),
                    Top = new StyleDimension(240f, 0f),
                    Height = new StyleDimension(-240f, 1f),
                    HAlign = 0.5f
                };
                Append(root);

                var panel = new UIPanel {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(-110f, 1f),
                    BackgroundColor = new Color(33, 43, 79) * 0.8f,
                    PaddingLeft = 0f,
                    PaddingRight = 0f
                };
                root.Append(panel);

                var listArea = new UIElement {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(-39f, 1f),
                    VAlign = 1f
                };
                listArea.SetPadding(0f);
                panel.Append(listArea);

                if (_addRemoveView)
                {
                    var packageContainer = new UIElement { Width = new StyleDimension(-20f, 1f), Height = StyleDimension.Fill, HAlign = 0.5f };
                    listArea.Append(packageContainer);
                    ConfigureList(_packageList, 0.5f);
                    packageContainer.Append(_packageList);
                    _packageTitle.HAlign = 0.5f;
                    _packageTitle.Width = new StyleDimension(-25f, 1f);
                    _packageTitle.Top = new StyleDimension(10f, 0f);
                    panel.Append(_packageTitle);
                    AddScrollbar(packageContainer, _packageList, 1f);
                }
                else
                {
                    var availableContainer = CreateColumn(0f, 10f);
                    var enabledContainer = CreateColumn(1f, -10f);
                    listArea.Append(availableContainer);
                    listArea.Append(enabledContainer);
                    ConfigureList(_availableList, 1f);
                    ConfigureList(_enabledList, 0f);
                    availableContainer.Append(_availableList);
                    enabledContainer.Append(_enabledList);
                    ConfigureTitle(_availableTitle, 0f, 25f);
                    ConfigureTitle(_enabledTitle, 1f, -25f);
                    panel.Append(_availableTitle);
                    panel.Append(_enabledTitle);
                    AddScrollbar(availableContainer, _availableList, 0f);
                    AddScrollbar(enabledContainer, _enabledList, 1f);
                    AddSeparator(panel);
                }

                var title = new UITextPanel<string>("Plugins", 1f, true) {
                    HAlign = 0.5f,
                    VAlign = 0f,
                    Top = new StyleDimension(-44f, 0f),
                    BackgroundColor = new Color(73, 94, 171)
                };
                title.SetPadding(13f);
                root.Append(title);

                AddBottomControls(root);
                RefreshLists();
            }

            private static UIElement CreateColumn(float align, float offset)
            {
                var column = new UIElement {
                    Width = new StyleDimension(-20f, 0.5f),
                    Height = StyleDimension.Fill,
                    HAlign = align,
                    Left = new StyleDimension(offset, 0f)
                };
                column.SetPadding(0f);
                return column;
            }

            private static void ConfigureList(UIList list, float align)
            {
                list.Width = new StyleDimension(-25f, 1f);
                list.Height = StyleDimension.Fill;
                list.ListPadding = 5f;
                list.HAlign = align;
            }

            private static void ConfigureTitle(UIText title, float align, float offset)
            {
                title.HAlign = align;
                title.Left = new StyleDimension(offset, 0f);
                title.Width = new StyleDimension(-25f, 0.5f);
                title.VAlign = 0f;
                title.Top = new StyleDimension(10f, 0f);
            }

            private static void AddScrollbar(UIElement container, UIList list, float align)
            {
                var scrollbar = new UIScrollbar((UIScrollbar.ColorTheme)0) {
                    Height = StyleDimension.Fill,
                    HAlign = align
                };
                container.Append(scrollbar);
                list.SetScrollbar(scrollbar);
            }

            private static void AddSeparator(UIPanel panel)
            {
                var separator = new UIVerticalSeparator {
                    Height = new StyleDimension(-12f, 1f),
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Color = new Color(89, 116, 213, 255) * 0.9f
                };
                panel.Append(separator);
            }

            private void AddBottomControls(UIElement root)
            {
                var footer = new UIElement { Width = StyleDimension.Fill, Height = new StyleDimension(50f, 0f), VAlign = 1f, Top = new StyleDimension(-45f, 0f) };
                root.Append(footer);
                var back = CreateFooterButton("Back", 0.7f, false, Close);
                PlaceFooterButton(back, 0);
                back.SetSnapPoint("GoBack", 0);
                footer.Append(back);

                var manage = CreateFooterButton("Manage Plugins", 0.48f, !_addRemoveView, () => { _addRemoveView = false; BuildPage(); });
                PlaceFooterButton(manage, 1);
                manage.SetSnapPoint("ManagePlugins", 0);
                footer.Append(manage);
                var addRemove = CreateFooterButton("Add / Remove Plugins", 0.34f, _addRemoveView, () => { _addRemoveView = true; BuildPage(); });
                PlaceFooterButton(addRemove, 2);
                addRemove.SetSnapPoint("AddRemovePlugins", 0);
                footer.Append(addRemove);

                var folder = CreateFooterButton("Open Folder", 0.48f, false, OpenPluginsFolder);
                PlaceFooterButton(folder, 3);
                folder.SetSnapPoint("OpenFolder", 0);
                footer.Append(folder);

                _settingsHint = new UIText("", 0.7f, false) {
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Top = new StyleDimension(-160f, 0f)
                };
                root.Append(_settingsHint);
            }

            private static void PlaceFooterButton(UIElement button, int column)
            {
                var width = new StyleDimension(-12f, 0.25f);
                button.Width = width;
                button.MinWidth = width;
                button.MaxWidth = width;
                button.Height = StyleDimension.Fill;
                button.Left = new StyleDimension(6f, column * 0.25f);
                button.HAlign = 0f;
                button.VAlign = 0f;
                button.OverflowHidden = true;
            }

            private static UITextPanel<string> CreateFooterButton(string text, float textScale, bool selected, Action activate)
            {
                var button = new UITextPanel<string>(text, textScale, large: true) {
                    BackgroundColor = selected ? new Color(73, 94, 171) : new Color(63, 82, 151) * 0.8f,
                    OverflowHidden = true
                };
                button.SetPadding(12f);
                button.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                button.OnMouseOut += (evt, element) => {
                    var panel = (UIPanel)element;
                    if (selected) {
                        panel.BackgroundColor = new Color(73, 94, 171);
                        panel.BorderColor = Color.Black;
                    }
                    else FadedMouseOut(panel);
                };
                button.OnLeftClick += (evt, element) => activate();
                return button;
            }

            private static void FadedMouseOver(UIPanel panel)
            {
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                panel.BackgroundColor = new Color(73, 94, 171);
                panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
            }

            private static void FadedMouseOut(UIPanel panel)
            {
                panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                panel.BorderColor = Color.Black;
            }

            private static void OpenPluginsFolder()
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                Directory.CreateDirectory(path);
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                Utils.OpenFolder(path);
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                RefreshRuntimeStatusHint(false);
                SetupGamepadPoints(spriteBatch);
            }

            private void SetupGamepadPoints(SpriteBatch spriteBatch)
            {
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 7;
                int startId = 3000;
                int nextId = startId;
                var allPoints = GetSnapPoints();

                if (_addRemoveView)
                {
                    SetupPackageGamepadPoints(spriteBatch, allPoints, startId, ref nextId);
                    return;
                }

                var availablePoints = _availableList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, availablePoints, _availableList);
                var enabledPoints = _enabledList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, enabledPoints, _enabledList);

                var availableDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "DescriptionOff");
                var availableToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, availablePoints, "ToggleToOn");
                var enabledDescription = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "DescriptionOn");
                var enabledSettings = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "SettingsOn");
                var enabledToggle = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, enabledPoints, "ToggleToOff");
                GetFooterLinkPoints(allPoints, ref nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder);

                // Disabled plugins intentionally have no settings action. Link their two actual actions directly.
                _gamepadHelper.LinkVerticalStrips(availableDescription, availableToggle, 0);
                _gamepadHelper.LinkVerticalStrips(availableToggle, enabledDescription, 0);
                _gamepadHelper.LinkVerticalStrips(enabledDescription, enabledSettings, 0);
                _gamepadHelper.LinkVerticalStrips(enabledSettings, enabledToggle, 0);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableToggle, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(availableDescription, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledToggle, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledSettings, folder);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(enabledDescription, folder);
                _gamepadHelper.MoveToVisuallyClosestPoint(startId, nextId);
            }

            private void SetupPackageGamepadPoints(SpriteBatch spriteBatch, List<SnapPoint> allPoints, int startId, ref int nextId)
            {
                var packagePoints = _packageList.GetSnapPoints();
                _gamepadHelper.CullPointsOutOfElementArea(spriteBatch, packagePoints, _packageList);
                var description = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, packagePoints, "PackageDescription");
                var uninstall = _gamepadHelper.GetVerticalStripFromCategoryName(ref nextId, packagePoints, "PackageUninstall");
                GetFooterLinkPoints(allPoints, ref nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder);

                _gamepadHelper.LinkVerticalStrips(description, uninstall, 0);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(description, back);
                _gamepadHelper.LinkVerticalStripBottomSideToSingle(uninstall, folder);
                LinkFooterStrip(back, manage, addRemove, folder);
                _gamepadHelper.MoveToVisuallyClosestPoint(startId, nextId);
            }

            private void GetFooterLinkPoints(List<SnapPoint> allPoints, ref int nextId, out UILinkPoint back, out UILinkPoint manage, out UILinkPoint addRemove, out UILinkPoint folder)
            {
                back = null;
                manage = null;
                addRemove = null;
                folder = null;
                foreach (SnapPoint point in allPoints)
                {
                    if (point.Name == "GoBack")
                        back = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "ManagePlugins")
                        manage = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "AddRemovePlugins")
                        addRemove = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                    else if (point.Name == "OpenFolder")
                        folder = _gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                }

                LinkFooterStrip(back, manage, addRemove, folder);
            }

            private void LinkFooterStrip(UILinkPoint back, UILinkPoint manage, UILinkPoint addRemove, UILinkPoint folder)
            {
                _gamepadHelper.PairLeftRight(back, manage);
                _gamepadHelper.PairLeftRight(manage, addRemove);
                _gamepadHelper.PairLeftRight(addRemove, folder);
            }

            private void RefreshLists()
            {
                _availableList.Clear();
                _enabledList.Clear();
                _packageList.Clear();
                int order = 0;
                foreach (PluginManagerRow plugin in _presenter.Present(_runtime, _diagnostics.ActiveWarnings))
                {
                    if (_addRemoveView)
                    {
                        _packageList.Add(CreatePackageRow(plugin, order++));
                        continue;
                    }
                    UIElement row = CreatePluginRow(plugin, order++);
                    if (plugin.IsEnabled)
                        _enabledList.Add(row);
                    else
                        _availableList.Add(row);
                }

                _availableTitle.SetText("Available Plugins (" + _availableList.Count + ")");
                _enabledTitle.SetText("Enabled Plugins (" + _enabledList.Count + ")");
                _packageTitle.SetText("Installed Plugins (" + _packageList.Count + ")");
            }

            private UIElement CreatePackageRow(PluginManagerRow plugin, int order)
            {
                var row = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(92f, 0f), BackgroundColor = ResourcePackBackground, BorderColor = ResourcePackBorder };
                row.SetPadding(5f);
                var name = new UIText(plugin.Name, 0.9f, false) { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(4f, 0f) };
                var author = new UIText(plugin.Author, 0.65f, false) { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(28f, 0f) };
                row.Append(name); row.Append(author);
                var content = new UIElement { Left = new StyleDimension(12f, 0f), Top = new StyleDimension(48f, 0f), Width = new StyleDimension(-24f, 1f), Height = new StyleDimension(-53f, 1f) };
                row.Append(content);
                var description = CreateDescriptionButton(plugin); description.Width = new StyleDimension(0f, plugin.IsEnabled ? 0.5f : 1f / 3f); description.Height = StyleDimension.Fill; description.SetSnapPoint("PackageDescription", order, null, null); description.OnLeftClick += (evt, element) => OpenDescription(plugin); content.Append(description);
                var status = CreatePackageStatusButton(); status.Left = StyleDimension.FromPercent(plugin.IsEnabled ? 0.5f : 1f / 3f); status.Width = new StyleDimension(0f, plugin.IsEnabled ? 0.5f : 1f / 3f); status.Height = StyleDimension.Fill; content.Append(status);
                if (!plugin.IsEnabled)
                {
                    var uninstall = CreateUninstallButton(); uninstall.Left = StyleDimension.FromPercent(2f / 3f); uninstall.Width = new StyleDimension(0f, 1f / 3f); uninstall.Height = StyleDimension.Fill; uninstall.SetSnapPoint("PackageUninstall", order, null, null); content.Append(uninstall);
                }
                return row;
            }

            private static UIResourcePackInfoButton<string> CreatePackageStatusButton()
            {
                var button = new UIResourcePackInfoButton<string>("", 0.8f, false) { IgnoresMouseInteraction = true };
                button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonCloudInactive"); button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText("Plugin is up to date"); }; return button;
            }

            private static UIResourcePackInfoButton<string> CreateUninstallButton()
            {
                var button = new UIResourcePackInfoButton<string>("", 0.8f, false); button.SetPadding(0f); AppendSmallIcon(button, "Images/UI/ButtonDelete"); button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText("Uninstall Plugin"); }; return button;
            }

            private static void AppendSmallIcon(UIElement button, string path)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture(path));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch (Exception exception)
                {
                    ReportOptionalUiFailure("Create plugin-manager icon", exception);
                }
            }

            private UIElement CreatePluginRow(PluginManagerRow plugin, int order)
            {
                var row = new UIPanel {
                    Width = StyleDimension.Fill,
                    Height = new StyleDimension(102f, 0f),
                    MinHeight = new StyleDimension(102f, 0f),
                    MaxHeight = new StyleDimension(102f, 0f),
                    BackgroundColor = ResourcePackBackground,
                    BorderColor = ResourcePackBorder
                };
                row.SetPadding(5f);
                row.OverflowHidden = true;
                row.OnMouseOver += (evt, element) => {
                    row.BackgroundColor = ResourcePackHoverBackground;
                    row.BorderColor = ResourcePackHoverBorder;
                };
                row.OnMouseOut += (evt, element) => {
                    row.BackgroundColor = ResourcePackBackground;
                    row.BorderColor = ResourcePackBorder;
                };

                var name = new UIText(plugin.Name, 1f, false) {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(4f, 0f)
                };
                row.Append(name);

                var author = new UIText(plugin.Author, 0.7f, false) {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(30f, 0f)
                };
                row.Append(author);

                var content = new UIElement {
                    Left = new StyleDimension(12f, 0f),
                    Top = new StyleDimension(50f, 0f),
                    Width = new StyleDimension(-24f, 1f),
                    Height = new StyleDimension(-55f, 1f)
                };
                row.Append(content);

                float buttonFraction = plugin.IsEnabled ? 1f / 3f : 0.5f;
                var description = CreateDescriptionButton(plugin);
                description.Width = new StyleDimension(0f, buttonFraction);
                description.Height = StyleDimension.Fill;
                description.SetSnapPoint(plugin.IsEnabled ? "DescriptionOn" : "DescriptionOff", order, null, null);
                description.OnLeftClick += (evt, element) => OpenDescription(plugin);
                content.Append(description);

                if (plugin.IsEnabled)
                {
                    var settings = CreateSettingsButton(plugin);
                    settings.Left = StyleDimension.FromPercent(buttonFraction);
                    settings.Width = new StyleDimension(0f, buttonFraction);
                    settings.Height = StyleDimension.Fill;
                    settings.SetSnapPoint("SettingsOn", order, null, null);
                    settings.OnLeftClick += (evt, element) => OpenSettings(plugin);
                    content.Append(settings);
                }

                var toggle = CreateToggleButton(plugin);
                toggle.Left = StyleDimension.FromPercent(plugin.IsEnabled ? 2f / 3f : 0.5f);
                toggle.Width = new StyleDimension(0f, buttonFraction);
                toggle.Height = StyleDimension.Fill;
                toggle.VAlign = 0f;
                toggle.SetSnapPoint(plugin.IsEnabled ? "ToggleToOff" : "ToggleToOn", order, null, null);
                if (plugin.CanToggle)
                {
                    toggle.OnLeftClick += (evt, element) => {
                        try
                        {
                            if (plugin.IsEnabled)
                                _runtime.Disable(plugin.Id);
                            else
                                _runtime.Enable(plugin.Id);
                            PersistEnabledPlugins();
                            RefreshRuntimeStatusHint(true);
                        }
                        catch (Exception exception)
                        {
                            _settingsHint.SetText("Unable to change " + plugin.Name + ": " + exception.Message);
                            _nextStatusRefreshUtc = DateTime.MaxValue;
                        }
                        RefreshLists();
                    };
                }
                else
                {
                    toggle.IgnoresMouseInteraction = true;
                    toggle.SetColorsBasedOnSelectionState(Color.Gray, Color.Gray, 0.55f, 0.55f);
                }
                content.Append(toggle);
                return row;
            }

            private void OpenSettings(PluginManagerRow plugin)
            {
                if (!HasSettings(plugin.Id))
                {
                    ShowStatus("No settings are exposed by " + plugin.Name + ".");
                    return;
                }
                Main.MenuUI.SetState(new PluginSettingsMenu(plugin, _extensions.GetSettingsControls(plugin.Id), _extensions.GetSettingsPages(plugin.Id)));
            }

            private void RefreshRuntimeStatusHint(bool force)
            {
                var now = DateTime.UtcNow;
                if (now < _manualHintExpiresUtc)
                    return;
                if (!force && now < _nextStatusRefreshUtc)
                    return;

                _nextStatusRefreshUtc = now.AddMilliseconds(250);
                var active = _notifications.GetActive(new DateTimeOffset(now));
                string text;
                if (active.Count > 0)
                    text = active[active.Count - 1].Message;
                else
                {
                    var warning = _diagnostics.ActiveWarnings.FirstOrDefault();
                    text = warning == null ? string.Empty : warning.Plugin + ": " + warning.Reason;
                }
                _settingsHint.SetText(text);
            }

            private void ShowStatus(string text)
            {
                _settingsHint.SetText(text);
                _manualHintExpiresUtc = DateTime.UtcNow.AddSeconds(4);
            }

            private static GroupOptionButton<bool> CreateToggleButton(PluginManagerRow plugin)
            {
                var toggle = new GroupOptionButton<bool>(
                    true,
                    null,
                    null,
                    Color.White,
                    null,
                    0.8f,
                    0.5f,
                    10f) {
                    ShowHighlightWhenSelected = false
                };
                toggle.SetColorsBasedOnSelectionState(Color.LightGreen, Color.PaleVioletRed, 0.7f, 0.7f);
                toggle.SetCurrentOption(plugin.IsEnabled);
                toggle.SetPadding(0f);

                AppendTexturePackIcon(toggle, plugin.IsEnabled);
                toggle.OnUpdate += (element) => DisplayMouseTextIfHovered(element, plugin.IsEnabled ? "Disable Plugin" : "Enable Plugin");
                return toggle;
            }

            private static UIResourcePackInfoButton<string> CreateDescriptionButton(PluginManagerRow plugin)
            {
                var description = new UIResourcePackInfoButton<string>("", 0.8f, false);
                description.SetPadding(0f);
                AppendPluginDescriptionIcon(description);
                description.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                description.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Description");
                return description;
            }

            private static UIResourcePackInfoButton<string> CreateSettingsButton(PluginManagerRow plugin)
            {
                var settings = new UIResourcePackInfoButton<string>("", 0.8f, false);
                settings.SetPadding(0f);
                AppendPluginSettingsIcon(settings);
                settings.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                settings.OnUpdate += (element) => DisplayMouseTextIfHovered(element, "Plugin Settings");
                return settings;
            }

            private static void DisplayMouseTextIfHovered(UIElement element, string text)
            {
                if (element.IsMouseHovering)
                    Main.instance.MouseText(text);
            }

            private static void AppendTexturePackIcon(UIElement button, bool enabled)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(
                        typeof(UIImageFramed),
                        RequestTexture("Images/UI/TexturePackButtons"),
                        GetTextureFrame("Images/UI/TexturePackButtons", 2, 2, enabled ? 0 : 1, 1));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void AppendPluginDescriptionIcon(UIElement button)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture("Images/UI/CharCreation/CharInfo"));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void AppendPluginSettingsIcon(UIElement button)
            {
                try
                {
                    var icon = (UIElement)Activator.CreateInstance(typeof(UIImage), RequestTexture("Images/UI/Camera_1"));
                    ConfigureButtonIcon(icon);
                    button.Append(icon);
                }
                catch
                {
                    // The button remains usable if optional artwork cannot be resolved.
                }
            }

            private static void ConfigureButtonIcon(UIElement icon)
            {
                icon.HAlign = 0.5f;
                icon.VAlign = 0.5f;
                icon.IgnoresMouseInteraction = true;
            }

            internal static object RequestTexture(string path)
            {
                object assets = GetMainAssets();
                if (_assetRequest == null)
                {
                    _assetRequest = assets.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method => {
                            ParameterInfo[] parameters = method.GetParameters();
                            return method.Name == "Request" &&
                                   method.IsGenericMethodDefinition &&
                                   parameters.Length == 2 &&
                                   parameters[0].ParameterType == typeof(string) &&
                                   parameters[1].ParameterType.IsEnum;
                        });
                }

                if (_assetRequest == null)
                    throw new MissingMethodException(assets.GetType().FullName, "Request<T>(string, AssetRequestMode)");

                Type modeType = _assetRequest.GetParameters()[1].ParameterType;
                return _assetRequest.MakeGenericMethod(typeof(Texture2D)).Invoke(assets, new object[] { path, Enum.ToObject(modeType, 1) });
            }

            private static Rectangle GetTextureFrame(string path, int horizontalFrames, int verticalFrames, int horizontalFrame, int verticalFrame)
            {
                object asset = RequestTexture(path);
                if (_assetFrame == null)
                {
                    _assetFrame = typeof(Utils).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(method => {
                            ParameterInfo[] parameters = method.GetParameters();
                            return method.Name == "Frame" &&
                                   parameters.Length == 7 &&
                                   parameters[0].ParameterType.IsGenericType &&
                                   parameters[0].ParameterType.GetGenericTypeDefinition().FullName == "ReLogic.Content.Asset`1";
                        });
                }

                if (_assetFrame == null)
                    throw new MissingMethodException(typeof(Utils).FullName, "Frame(Asset<T>, int, int, int, int, int, int)");

                return (Rectangle)_assetFrame.Invoke(null, new object[] { asset, horizontalFrames, verticalFrames, horizontalFrame, verticalFrame, 0, 0 });
            }
        }

        private sealed class PluginSettingsMenu : UIState
        {
            private readonly PluginManagerRow plugin;
            private readonly IReadOnlyList<PluginSettingControl> controls;
            private readonly IReadOnlyList<PluginUiContribution> legacyPages;
            private UIGamepadHelper gamepadHelper;

            public PluginSettingsMenu(PluginManagerRow plugin, IReadOnlyList<PluginSettingControl> controls, IReadOnlyList<PluginUiContribution> legacyPages)
            {
                this.plugin = plugin;
                this.controls = controls;
                this.legacyPages = legacyPages;
            }

            public override void OnInitialize()
            {
                // This deliberately mirrors UIManageControls: Terraria owns the panel, list, slider, and scrollbar visuals.
                var outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(600f, 0f), Top = new StyleDimension(220f, 0f), Height = new StyleDimension(-200f, 1f), HAlign = 0.5f };
                Append(outer);
                var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-110f, 1f), BackgroundColor = new Color(33, 43, 79) * 0.8f };
                outer.Append(panel);
                var list = new UIList { Width = new StyleDimension(-25f, 1f), Height = new StyleDimension(-50f, 1f), VAlign = 1f, PaddingBottom = 5f, ListPadding = 20f };
                panel.Append(list);

                int snapIndex = 0;
                foreach (var control in controls)
                    AddControl(list, control, snapIndex++);
                foreach (var page in legacyPages.Where(contribution => contribution.IsInteractive))
                    AddLegacyControl(list, page, snapIndex++);

                var scrollbar = new UIScrollbar { Height = new StyleDimension(-67f, 1f), HAlign = 1f, VAlign = 1f, MarginBottom = 11f };
                panel.Append(scrollbar);
                list.SetScrollbar(scrollbar);

                var title = new UITextPanel<string>(plugin.Name + " Settings", 0.7f, true) { HAlign = 0.5f, Top = new StyleDimension(-45f, 0f), Left = new StyleDimension(-10f, 0f), BackgroundColor = new Color(73, 94, 171) };
                title.SetPadding(15f);
                outer.Append(title);
                var back = new UITextPanel<string>("Back", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f), Height = new StyleDimension(50f, 0f),
                    HAlign = 0.5f, VAlign = 1f, Top = new StyleDimension(-20f, 0f)
                };
                back.OnMouseOver += (evt, element) => {
                    var button = (UIPanel)element;
                    button.BackgroundColor = new Color(73, 94, 171);
                    button.BorderColor = Colors.FancyUIFatButtonMouseOver;
                };
                back.OnMouseOut += (evt, element) => {
                    var button = (UIPanel)element;
                    button.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                    button.BorderColor = Color.Black;
                };
                back.OnLeftClick += (evt, element) => ReturnToPluginList();
                back.SetSnapPoint("GoBack", 0, null, null);
                outer.Append(back);
            }

            private static void AddControl(UIList list, PluginSettingControl control, int snapIndex)
            {
                if (control.Kind == PluginSettingControlKind.Slider)
                {
                    var slider = new UIKeybindingSliderItem(
                        () => control.DisplayName + ": " + ReadSettingValue(control),
                        () => Normalize(control.GetSlider(), control.Minimum, control.Maximum),
                        value => control.SetSlider(Denormalize(value, control.Minimum, control.Maximum, control.Step)),
                        () => { }, control.Id.GetHashCode(), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(34f, 0f) };
                    slider.SetSnapPoint("PluginSetting", snapIndex, null, null);
                    list.Add(slider);
                    return;
                }
                if (control.Kind == PluginSettingControlKind.Color)
                {
                    AddColorControls(list, control, snapIndex);
                    return;
                }
                var entry = new UIKeybindingSimpleListItem(() => control.DisplayName + ": " + ReadSettingValue(control), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f) };
                entry.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                entry.OnLeftClick += (evt, element) => ActivateSetting(control);
                entry.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(entry);
            }

            private static void AddLegacyControl(UIList list, PluginUiContribution contribution, int snapIndex)
            {
                var entry = new UIKeybindingSimpleListItem(() => contribution.DisplayName + ": " + ReadSettingValue(contribution), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f) };
                entry.OnMouseOver += (evt, element) => SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                entry.OnLeftClick += (evt, element) => ActivateSetting(contribution);
                entry.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(entry);
            }

            private static void AddColorControls(UIList list, PluginSettingControl control, int snapIndex)
            {
                var row = new UIKeybindingSimpleListItem(() => control.DisplayName + ": " + control.GetColor().ToHex(), new Color(73, 94, 171, 255) * 0.9f) { Width = StyleDimension.Fill, Height = new StyleDimension(38f, 0f) };
                var swatch = new UIPanel { Width = new StyleDimension(20f, 0f), Height = new StyleDimension(20f, 0f), HAlign = 1f, VAlign = 0.5f, Left = new StyleDimension(-72f, 0f), IgnoresMouseInteraction = true };
                swatch.OnUpdate += element => ((UIPanel)element).BackgroundColor = new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue);
                row.Append(swatch);
                row.Append(CreateClipboardIcon("Images/UI/CharCreation/Copy", -46f, "Copy color hex", () => TrySetClipboardText(control.GetColor().ToHex())));
                row.Append(CreateClipboardIcon("Images/UI/CharCreation/Paste", -22f, "Paste color hex", () => { if (PluginColor.TryParseHex(TryGetClipboardText(), out var value)) control.SetColor(value); }));
                row.SetSnapPoint("PluginSetting", snapIndex, null, null);
                list.Add(row);
            }

            private static UIElement CreateClipboardIcon(string assetPath, float offset, string hoverText, Action click)
            {
                // 20px is 65% of Terraria's small character-creation button, keeping the action icons inside this compact row.
                var button = new UIPanel { Width = new StyleDimension(20f, 0f), Height = new StyleDimension(20f, 0f) };
                button.SetPadding(0f);
                button.HAlign = 1f;
                button.VAlign = 0.5f;
                button.Left = new StyleDimension(offset, 0f);
                var image = (UIElement)Activator.CreateInstance(typeof(UIImage), PluginSelectionMenu.RequestTexture(assetPath));
                image.Width = StyleDimension.Fill;
                image.Height = StyleDimension.Fill;
                image.IgnoresMouseInteraction = true;
                typeof(UIImage).GetProperty("ScaleToFit", BindingFlags.Public | BindingFlags.Instance)?.SetValue(image, true, null);
                button.Append(image);
                button.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                button.BorderColor = Color.Black;
                button.OnMouseOver += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(73, 94, 171);
                    panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
                    SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                };
                button.OnMouseOut += (evt, element) => {
                    var panel = (UIPanel)element;
                    panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                    panel.BorderColor = Color.Black;
                };
                button.OnUpdate += element => { if (element.IsMouseHovering) Main.instance.MouseText(hoverText); };
                button.OnLeftClick += (evt, element) => { click(); SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f); };
                return button;
            }

            private static float Normalize(float value, float min, float max) => MathHelper.Clamp((value - min) / (max - min), 0f, 1f);
            private static float Denormalize(float value, float min, float max, float step)
            {
                float result = min + MathHelper.Clamp(value, 0f, 1f) * (max - min);
                return step <= 0f ? result : min + (float)Math.Round((result - min) / step) * step;
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
                SetupGamepadPoints();
            }

            private void SetupGamepadPoints()
            {
                int firstId = 3600;
                int nextId = firstId;
                foreach (var point in GetSnapPoints().Where(point => point.Name == "PluginSetting" || point.Name == "GoBack"))
                    gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                gamepadHelper.MoveToVisuallyClosestPoint(firstId, nextId);
            }

        }

        private sealed class PluginDescriptionMenu : UIState
        {
            private readonly PluginManagerRow _plugin;
            private UIGamepadHelper gamepadHelper;

            public PluginDescriptionMenu(PluginManagerRow plugin)
            {
                _plugin = plugin;
            }

            public override void OnInitialize()
            {
                // Mirrors UIResourcePackInfoMenu so variable package text is measured and scrolls instead of clipping.
                var outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(500f, 0f), MinWidth = new StyleDimension(300f, 0f), Top = new StyleDimension(230f, 0f), Height = new StyleDimension(-230f, 1f), HAlign = 0.5f };
                Append(outer);
                var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-110f, 1f), BackgroundColor = new Color(33, 43, 79) * 0.8f };
                outer.Append(panel);
                var content = new UIElement { Width = StyleDimension.Fill, Height = StyleDimension.Fill };
                panel.Append(content);

                var title = new UIText(_plugin.Name, 0.935f, true) {
                    HAlign = 0.5f,
                    Top = new StyleDimension(0f, 0f)
                };
                content.Append(title);

                var author = new UIText("Author: " + _plugin.Author, 0.8f, false) {
                    HAlign = 0f, VAlign = 0f, Top = new StyleDimension(42f, 0f)
                };
                content.Append(author);

                var version = new UIText("Version: " + _plugin.Version, 0.8f, false) {
                    HAlign = 1f, VAlign = 0f, Top = new StyleDimension(42f, 0f)
                };
                content.Append(version);

                var list = new UIList { Width = new StyleDimension(-25f, 1f), Height = new StyleDimension(-112f, 1f), VAlign = 1f, ListPadding = 14f, PaddingRight = 12f, ManualSortMethod = items => { } };
                list.Add(CreateSection("Description", _plugin.Description, true));
                list.Add(CreateSection("Changelog", _plugin.Changelog, false));
                content.Append(list);
                var scrollbar = new UIScrollbar { Height = new StyleDimension(-112f, 1f), HAlign = 1f, VAlign = 1f };
                content.Append(scrollbar);
                list.SetScrollbar(scrollbar);

                var back = new UITextPanel<string>("Back", 0.7f, true) {
                    Width = new StyleDimension(-8f, 0.5f),
                    Height = new StyleDimension(50f, 0f),
                    HAlign = 0.5f,
                    VAlign = 1f,
                    Top = new StyleDimension(-20f, 0f)
                };
                back.OnMouseOver += (evt, element) => FadedMouseOver((UIPanel)element);
                back.OnMouseOut += (evt, element) => FadedMouseOut((UIPanel)element);
                back.OnLeftClick += (evt, element) => ReturnToPluginList();
                back.SetSnapPoint("GoBack", 0, null, null);
                outer.Append(back);
            }

            private static UIElement CreateSection(string heading, string text, bool includeDivider)
            {
                string value = string.IsNullOrWhiteSpace(text) ? "No information provided." : text;
                int lines = Math.Max(1, (value.Length + 45) / 46);
                float bodyHeight = lines * 19f;
                float dividerSpace = includeDivider ? 14f : 0f;
                var section = new UIElement { Width = StyleDimension.Fill, Height = new StyleDimension(38f + bodyHeight + dividerSpace, 0f) };
                section.Append(new UIText(heading, 0.68f, true) { Width = StyleDimension.Fill, Height = new StyleDimension(20f, 0f) });
                section.Append(new UIText(value, 0.75f, false) { Width = StyleDimension.Fill, Top = new StyleDimension(30f, 0f), IsWrapped = true, WrappedTextBottomPadding = 0f });
                if (includeDivider)
                {
                    var divider = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(2f, 0f), Top = new StyleDimension(34f + bodyHeight, 0f), BackgroundColor = new Color(104, 123, 192) * 0.65f, BorderColor = Color.Transparent, IgnoresMouseInteraction = true };
                    divider.SetPadding(0f);
                    section.Append(divider);
                }
                return section;
            }

            public override void Draw(SpriteBatch spriteBatch)
            {
                base.Draw(spriteBatch);
                UILinkPointNavigator.Shortcuts.BackButtonCommand = 1;
                int firstId = 3700;
                int nextId = firstId;
                foreach (var point in GetSnapPoints().Where(point => point.Name == "GoBack"))
                    gamepadHelper.MakeLinkPointFromSnapPoint(nextId++, point);
                gamepadHelper.MoveToVisuallyClosestPoint(firstId, nextId);
            }

            private static void FadedMouseOver(UIPanel panel)
            {
                SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f);
                panel.BackgroundColor = new Color(73, 94, 171);
                panel.BorderColor = Colors.FancyUIFatButtonMouseOver;
            }

            private static void FadedMouseOut(UIPanel panel)
            {
                panel.BackgroundColor = new Color(63, 82, 151) * 0.8f;
                panel.BorderColor = Color.Black;
            }
        }

        private sealed class BridgePluginLogger : IPluginLogger
        {
            private readonly PluginId plugin;
            public BridgePluginLogger(PluginId plugin) { this.plugin = plugin; }
            public void Debug(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Info(string message) { System.Diagnostics.Trace.WriteLine("[Alacrity:" + plugin + "] " + message); }
            public void Warn(string message) { System.Diagnostics.Trace.TraceWarning("[Alacrity:" + plugin + "] " + message); }
            public void Error(string message, Exception exception = null) { System.Diagnostics.Trace.TraceError("[Alacrity:" + plugin + "] " + message + (exception == null ? string.Empty : " " + exception)); }
        }

        private sealed class TerrariaMultiplayerSession : IMultiplayerSession
        {
            public bool IsConnected { get { return Main.netMode == 1; } }
            public bool IsVanillaCompatibleMode { get { return true; } }
            public bool IsAlacrityAwareServer { get { return false; } }
            public ServerIdentity Server { get { return null; } }
            public ServerPluginPolicySnapshot ActivePolicy { get { return null; } }
        }
    }
}
