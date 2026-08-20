using System;
using System.Reflection;
using AlacrityTerraria.UserInterface;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;

namespace AlacrityTerraria.UserInterface.Banners;

/// <summary>
/// Owns the banner-claiming window's local search state. It filters only the compact native
/// claimable-banner array, never Terraria's complete banner catalog.
/// </summary>
internal sealed class BannerSearchPresenter
{
    private const int SearchWidth = 164;
    private const int SearchHeight = 22;

    private readonly PluginSearchTextBuffer searchText = new PluginSearchTextBuffer();

    private PluginSearchKeyRepeatState backspaceRepeat;
    private PluginSearchKeyRepeatState deleteRepeat;
    private PluginSearchKeyRepeatState leftRepeat;
    private PluginSearchKeyRepeatState rightRepeat;
    private KeyboardState previousKeyboard;
    private bool hasPreviousKeyboard;
    private bool isFocused;
    private uint[] matchUpdates = Array.Empty<uint>();
    private int[] matchVersions = Array.Empty<int>();
    private bool[] matchResults = Array.Empty<bool>();
    private int searchVersion = 1;
    private bool pixelResolved;
    private Texture2D pixel;

    internal bool HasActiveFilter => !string.IsNullOrEmpty(searchText.Text);

    internal bool MatchesAvailableBanner(int bannerIndex)
    {
        if (string.IsNullOrEmpty(searchText.Text))
        {
            return true;
        }

        try
        {
            int itemType = BannerSystem.BannerToItem(bannerIndex);
            if (itemType <= 0 || !ContentSamples.ItemsByType.TryGetValue(itemType, out Item bannerItem))
            {
                return true;
            }

            EnsureMatchCapacity(bannerIndex);
            uint update = Main.GameUpdateCount;
            if (matchUpdates[bannerIndex] == update && matchVersions[bannerIndex] == searchVersion)
            {
                return matchResults[bannerIndex];
            }

            string name = bannerItem.Name ?? string.Empty;
            bool matches = name.IndexOf(searchText.Text, StringComparison.OrdinalIgnoreCase) >= 0;
            matchUpdates[bannerIndex] = update;
            matchVersions[bannerIndex] = searchVersion;
            matchResults[bannerIndex] = matches;
            return matches;
        }
        catch
        {
            // Search presentation must never hide a valid native banner entry on failure.
            return true;
        }
    }

    internal void Draw(SpriteBatch spriteBatch, int x, int y)
    {
        if (spriteBatch == null)
        {
            return;
        }

        var bounds = new Rectangle(Math.Max(4, x), Math.Max(4, y), SearchWidth, SearchHeight);
        bool hovered = bounds.Contains(Main.mouseX, Main.mouseY);
        if (hovered && Main.mouseLeft && Main.mouseLeftRelease)
        {
            isFocused = true;
            Main.mouseLeftRelease = false;
        }
        else if (!hovered && Main.mouseLeft && Main.mouseLeftRelease)
        {
            Blur();
        }

        if (isFocused)
        {
            PlayerInput.WritingText = true;
            Main.instance?.HandleIME();
            KeyboardState current = Keyboard.GetState();
            KeyboardState previous = hasPreviousKeyboard ? previousKeyboard : current;
            hasPreviousKeyboard = true;
            previousKeyboard = current;
            if (Pressed(current, previous, Keys.Escape))
            {
                Blur();
            }
            else
            {
                ProcessInput(current, previous);
            }
        }

        Texture2D texture = GetPixel();
        if (texture == null)
        {
            return;
        }

        Color background = hovered || isFocused ? new Color(52, 56, 80, 228) : new Color(31, 33, 49, 218);
        Color border = isFocused ? new Color(184, 158, 88, 238) : new Color(102, 108, 142, 224);
        spriteBatch.Draw(texture, bounds, background);
        spriteBatch.Draw(texture, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), border);
        spriteBatch.Draw(texture, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), border);
        spriteBatch.Draw(texture, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), border);
        spriteBatch.Draw(texture, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), border);

        string value = searchText.Text;
        string display = string.IsNullOrEmpty(value) ? "Search banners..." : value;
        Color textColor = string.IsNullOrEmpty(value) ? Color.Gray : Color.White;
        DrawSelection(spriteBatch, texture, bounds, value);
        Utils.DrawBorderString(spriteBatch, display, new Vector2(bounds.X + 5, bounds.Center.Y), textColor, 0.68f, 0f, 0.5f, -1);
        if (isFocused && Main.instance != null && Main.instance.textBlinkerState == 1)
        {
            float width = Utils.DrawBorderString(spriteBatch, value.Substring(0, searchText.Caret), Vector2.Zero, Color.Transparent, 0.68f).X;
            spriteBatch.Draw(texture, new Rectangle(bounds.X + 5 + (int)width, bounds.Y + 4, 1, bounds.Height - 8), Color.White);
        }
    }

    private void ProcessInput(KeyboardState current, KeyboardState previous)
    {
        string before = searchText.Text;
        bool control = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        if (control && Pressed(current, previous, Keys.A))
        {
            searchText.SelectAll();
        }
        else if (backspaceRepeat.ShouldRepeat(current, previous, Keys.Back))
        {
            searchText.Backspace(control);
        }
        else if (deleteRepeat.ShouldRepeat(current, previous, Keys.Delete))
        {
            searchText.Delete(control);
        }
        else if (leftRepeat.ShouldRepeat(current, previous, Keys.Left))
        {
            searchText.MoveLeft(control, shift);
        }
        else if (rightRepeat.ShouldRepeat(current, previous, Keys.Right))
        {
            searchText.MoveRight(control, shift);
        }
        else if (Pressed(current, previous, Keys.Home))
        {
            searchText.MoveHome(shift);
        }
        else if (Pressed(current, previous, Keys.End))
        {
            searchText.MoveEnd(shift);
        }
        else
        {
            int count = Math.Max(0, Math.Min(Main.keyCount, Math.Min(Main.keyInt.Length, Main.keyString.Length)));
            for (int index = 0; index < count; index++)
            {
                int key = Main.keyInt[index];
                string value = Main.keyString[index];
                if (key >= 32 && key != 127 &&
                    searchText.TryBuildInsertedText(value, out string candidate) &&
                    HasAvailableMatch(candidate))
                {
                    searchText.Insert(value);
                }
            }
        }

        Main.keyCount = 0;
        if (!string.Equals(before, searchText.Text, StringComparison.Ordinal))
        {
            searchVersion = searchVersion == int.MaxValue ? 1 : searchVersion + 1;
        }
    }

    private Texture2D GetPixel()
    {
        if (pixelResolved)
        {
            return pixel;
        }

        pixelResolved = true;
        try
        {
            Type textureAssets = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets", false);
            FieldInfo magicPixel = textureAssets?.GetField("MagicPixel", BindingFlags.Public | BindingFlags.Static);
            object asset = magicPixel?.GetValue(null);
            PropertyInfo value = asset?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            pixel = value?.GetValue(asset, null) as Texture2D;
        }
        catch
        {
            pixel = null;
        }

        return pixel;
    }

    private void DrawSelection(SpriteBatch spriteBatch, Texture2D texture, Rectangle bounds, string value)
    {
        if (!isFocused || !searchText.TryGetSelection(out int start, out int end))
        {
            return;
        }

        float left = Utils.DrawBorderString(spriteBatch, value.Substring(0, start), Vector2.Zero, Color.Transparent, 0.68f).X;
        float right = Utils.DrawBorderString(spriteBatch, value.Substring(0, end), Vector2.Zero, Color.Transparent, 0.68f).X;
        int width = Math.Max(1, (int)Math.Ceiling(right - left));
        spriteBatch.Draw(texture, new Rectangle(bounds.X + 5 + (int)left, bounds.Y + 4, width, bounds.Height - 8), new Color(96, 142, 218, 150));
    }

    private void EnsureMatchCapacity(int bannerIndex)
    {
        if (bannerIndex < matchUpdates.Length)
        {
            return;
        }

        int length = Math.Max(bannerIndex + 1, Math.Max(64, matchUpdates.Length * 2));
        Array.Resize(ref matchUpdates, length);
        Array.Resize(ref matchVersions, length);
        Array.Resize(ref matchResults, length);
    }

    private static bool HasAvailableMatch(string query)
    {
        ushort[] claimableCounts = BannerSystem.GetClaimableBannerCounts();
        for (int bannerIndex = 1; bannerIndex < claimableCounts.Length; bannerIndex++)
        {
            if (claimableCounts[bannerIndex] == 0)
            {
                continue;
            }

            int itemType = BannerSystem.BannerToItem(bannerIndex);
            if (itemType > 0 &&
                ContentSamples.ItemsByType.TryGetValue(itemType, out Item bannerItem) &&
                (bannerItem.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private void Blur()
    {
        isFocused = false;
        hasPreviousKeyboard = false;
        PlayerInput.WritingText = false;
        Main.instance?.HandleIME();
    }

    private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }
}
