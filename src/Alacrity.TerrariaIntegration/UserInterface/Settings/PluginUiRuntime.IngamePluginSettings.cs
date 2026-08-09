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
using Terraria;
using Terraria.Audio;

namespace AlacrityTerraria
{
    /// <summary>
    /// In-game settings-page rendering and immediate-mode helper implementation. This remains a
    /// partial bridge type only because the existing patched ABI calls its facade methods.
    /// </summary>
    public static partial class PluginUiRuntime
    {        private static void DrawIngamePluginSettingsPage(SpriteBatch spriteBatch, Rectangle bounds, PluginManagerRow plugin)
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
                var swatch = new Rectangle(bounds.X + 18, y - 4, 20, 20);
                Utils.DrawInvBG(spriteBatch, swatch.X, swatch.Y, swatch.Width, swatch.Height, new Color(control.GetColor().Red, control.GetColor().Green, control.GetColor().Blue));
                Utils.DrawBorderString(spriteBatch, control.DisplayName, new Vector2(bounds.X + 46, y), Color.White, 0.7f, 0f, 0f, -1);
                var copy = new Rectangle(bounds.Right - 73, y - 5, 25, 22);
                var paste = new Rectangle(bounds.Right - 42, y - 5, 25, 22);
                bool copyHover = copy.Contains(Main.mouseX, Main.mouseY), pasteHover = paste.Contains(Main.mouseX, Main.mouseY);
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


    }
}
