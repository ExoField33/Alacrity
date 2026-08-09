using System;
using System.Collections.Generic;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;

namespace AlacrityTerraria
{
    /// <summary>In-game Plugins-page list and description rendering, isolated from the patch ABI facade.</summary>
    public static partial class PluginUiRuntime
    {
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
                    ShowHoverText("No plugin settings are available.");
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
                Utils.DrawBorderString(spriteBatch, lines[index], new Vector2(bounds.X + 18, bounds.Y + top + index * lineHeight - _ingameDescriptionScroll), Color.White, heading ? 0.8f : 0.65f, 0f, 0f, -1);
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
    }
}
