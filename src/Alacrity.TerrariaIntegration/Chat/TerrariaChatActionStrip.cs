using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Alacrity.Core;
using Alacrity.PluginSdk;

namespace AlacrityTerraria.Chat;

/// <summary>Terraria-owned renderer for generic plugin chat actions. It runs only while the player
/// chat field is open, keeps all pointer handling in the host, and never gives plugins SpriteBatch
/// or chat-monitor access.</summary>
internal static class TerrariaChatActionStrip
{
    private const int ButtonSpacing = 4;
    // Terraria's player-chat texture begins 36px from the bottom, but its visible interior is
    // slightly smaller. A 34px action surface aligns its visible bottom with that input surface.
    private const int ChatInputHeight = 36;
    private const int ChatActionButtonHeight = 34;
    private const int ChatActionButtonBottomInset = -1;
    private const int MenuWidth = 270;
    private const int MenuRowHeight = 28;
    private const int MenuSearchHeight = 26;
    private const int MaximumVisibleMenuRows = 10;
    private static ContentManager content;
    private static readonly Dictionary<ChatActionTextureKey, Texture2D> textures = new Dictionary<ChatActionTextureKey, Texture2D>();
    private static readonly HashSet<ChatActionTextureKey> missingTextures = new HashSet<ChatActionTextureKey>();
    private static PluginId openOwner;
    private static string openButtonId;
    private static readonly List<string> openMenuPath = new List<string>(2);
    private static readonly List<ChatActionMenuItem> filteredMenuItems = new List<ChatActionMenuItem>(128);
    private static int menuScroll;
    private static int menuTop = -1;
    private static int menuBottom = -1;
    private static int menuTopScreenHeight = -1;
    private static string menuSearch = string.Empty;
    private static readonly UserInterface.PluginSearchTextBuffer menuSearchBuffer = new UserInterface.PluginSearchTextBuffer();
    private static UserInterface.PluginSearchKeyRepeatState menuSearchBackspaceRepeat;
    private static UserInterface.PluginSearchKeyRepeatState menuSearchDeleteRepeat;
    private static UserInterface.PluginSearchKeyRepeatState menuSearchLeftRepeat;
    private static UserInterface.PluginSearchKeyRepeatState menuSearchRightRepeat;
    private static bool menuSearchFocused;
    private static PluginId hoveredOwner;
    private static string hoveredButtonId;
    private static string hoveredMenuItemId;
    private static GraphicsDevice dualToneBackgroundDevice;
    private static Texture2D dualToneBackground;
    private static Texture2D dualToneSource;
    private static Color[] dualToneSourcePixels;
    private static Color dualTonePrimary;
    private static Color dualToneSecondary;
    private static bool dualToneBackgroundColorsSet;
    private static bool wasLeftDown;
    private static bool wasRightDown;

    /// <summary>Whether a generic host-owned action popover currently needs focused chat input.</summary>
    internal static bool IsOpen => openOwner.IsValid && !string.IsNullOrEmpty(openButtonId);

    /// <summary>Consumes typed input while the menu's host-owned search field has focus, leaving
    /// the player's chat text untouched until the search field is dismissed.</summary>
    internal static bool TryProcessSearchInput(KeyboardState current, KeyboardState previous)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (!CanSearchOpenMenu())
        {
            return false;
        }

        if (IsMenuEscapePressed(current, previous))
        {
            if (menuSearchFocused)
            {
                menuSearchFocused = false;
                Main.inputTextEscape = false;
                Main.keyCount = 0;
                return true;
            }

            return false;
        }

        if (!menuSearchFocused)
        {
            if (!ShouldStartMenuSearch(current, previous))
            {
                return false;
            }

            menuSearchFocused = true;
        }

        if (Main.keyState.IsKeyDown(Keys.Enter) && !Main.oldKeyState.IsKeyDown(Keys.Enter))
        {
            menuSearchFocused = false;
            Main.inputTextEnter = false;
        }
        else
        {
            ProcessMenuSearchKeys(current, previous);
            menuSearch = menuSearchBuffer.Text;
        }

        Main.keyCount = 0;
        return true;
    }

    /// <summary>
    /// Lets an open host-owned action menu claim the wheel before a chat editor or Terraria's
    /// hotbar input processes it. Rendering happens later in the frame, so draw-time handling
    /// alone is too late when another chat feature owns scrolling.
    /// </summary>
    internal static bool TryConsumeScrollWheel(PluginChatHost chat)
    {
        if (chat == null || !Main.drawingPlayerChat || !IsOpen || PlayerInput.ScrollWheelDelta == 0)
        {
            return false;
        }

        IReadOnlyList<ChatActionButtonView> buttons = chat.GetActionButtons();
        if (buttons.Count == 0)
        {
            Close();
            return false;
        }

        IReadOnlyList<ChatActionMenuItem> rootItems = chat.GetActionButtonMenuItems(openOwner, openButtonId);
        IReadOnlyList<ChatActionMenuItem> unfilteredItems = ResolveOpenMenuItems(rootItems, out string title, out ChatActionMenuDirection direction);
        if (unfilteredItems.Count == 0)
        {
            Close();
            return false;
        }

        bool showSearch = CanSearchOpenMenu();
        IReadOnlyList<ChatActionMenuItem> items = GetFilteredMenuItems(unfilteredItems, showSearch);
        Rectangle bounds = GetMenuBounds(items.Count, title, GetActionButtonX(buttons.Count), GetActionButtonY(), ChatActionButtonHeight, showSearch, direction);
        return TryConsumeMenuScroll(items.Count, bounds);
    }

    internal static void Draw(PluginChatHost chat)
    {
        if (chat == null || !Main.drawingPlayerChat || !chat.HasActionButtons || Main.spriteBatch == null)
        {
            Close();
            return;
        }

        IReadOnlyList<ChatActionButtonView> buttons = chat.GetActionButtons();
        if (buttons.Count == 0)
        {
            Close();
            return;
        }

        bool pressed = Main.mouseLeft && !wasLeftDown;
        wasLeftDown = Main.mouseLeft;
        bool rightPressed = Main.mouseRight && !wasRightDown;
        wasRightDown = Main.mouseRight;
        int buttonSize = ChatActionButtonHeight;
        int y = GetActionButtonY();
        int x = GetActionButtonX(buttons.Count);
        bool consumed = false;
        PluginId currentHoveredOwner = default;
        string currentHoveredButtonId = null;

        for (int index = 0; index < buttons.Count; index++)
        {
            ChatActionButtonView view = buttons[index];
            Rectangle bounds = new Rectangle(x + index * (buttonSize + ButtonSpacing), y, buttonSize, buttonSize);
            bool hovered = bounds.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
            bool isOpen = openOwner == view.Owner && string.Equals(openButtonId, view.Descriptor.Id, StringComparison.Ordinal);
            chat.TryGetActionButtonVisualState(view.Owner, view.Descriptor.Id, out ChatActionButtonVisualState visual);
            DrawButton(Main.spriteBatch, bounds, view, hovered, visual);

            if (!hovered)
            {
                continue;
            }

            currentHoveredOwner = view.Owner;
            currentHoveredButtonId = view.Descriptor.Id;
            Main.LocalPlayer.mouseInterface = true;
            if (view.Descriptor.Tooltip != null)
            {
                PluginUiRuntime.ShowHoverText(view.Descriptor.Tooltip.Text);
            }

            bool shift = Main.keyState.PressingShift();
            bool quickRightClick = rightPressed && shift;
            if ((!pressed && !quickRightClick) || consumed)
            {
                continue;
            }

            ChatActionButtonMouseButton button = pressed ? ChatActionButtonMouseButton.Left : ChatActionButtonMouseButton.Right;
            if (chat.TryActivateActionButton(view.Owner, view.Descriptor.Id, button, shift))
            {
                consumed = true;
                if (button == ChatActionButtonMouseButton.Left)
                {
                    Main.mouseLeftRelease = false;
                }
                else
                {
                    Main.mouseRightRelease = false;
                }

                if (shift)
                {
                    Close();
                    SoundEngine.PlaySound(12);
                }
                else if (isOpen)
                {
                    Close();
                    SoundEngine.PlaySound(11);
                }
                else
                {
                    openOwner = view.Owner;
                    openButtonId = view.Descriptor.Id;
                    openMenuPath.Clear();
                    ResetMenuSearch();
                    ResetMenuAnchor();
                    SoundEngine.PlaySound(10);
                }
            }
        }

        UpdateButtonHoverSound(currentHoveredOwner, currentHoveredButtonId);
        DrawOpenMenu(chat, pressed, ref consumed, x - MenuWidth - 8, y, buttonSize);
    }

    /// <summary>Consumes Escape before Terraria closes player chat. One press backs out of a nested
    /// chooser; another closes the action popover and leaves the chat field active.</summary>
    internal static bool TryHandleEscape()
    {
        if (!openOwner.IsValid || string.IsNullOrEmpty(openButtonId))
        {
            return false;
        }

        if (openMenuPath.Count != 0)
        {
            openMenuPath.RemoveAt(openMenuPath.Count - 1);
            ResetMenuSearch();
            ResetMenuAnchor();
        }
        else
        {
            Close();
        }

        SoundEngine.PlaySound(11);
        return true;
    }

    internal static void Reset()
    {
        Close();
        wasLeftDown = false;
        wasRightDown = false;
        hoveredOwner = default;
        hoveredButtonId = null;
        hoveredMenuItemId = null;
        if (content != null)
        {
            content.Dispose();
            content = null;
        }

        textures.Clear();
        missingTextures.Clear();
        DisposeDualToneBackgrounds();
    }

    private static void DrawOpenMenu(PluginChatHost chat, bool pressed, ref bool consumed, int desiredX, int y, int buttonSize)
    {
        if (!openOwner.IsValid || string.IsNullOrEmpty(openButtonId))
        {
            return;
        }

        IReadOnlyList<ChatActionMenuItem> rootItems = chat.GetActionButtonMenuItems(openOwner, openButtonId);
        IReadOnlyList<ChatActionMenuItem> unfilteredItems = ResolveOpenMenuItems(rootItems, out string title, out ChatActionMenuDirection direction);
        if (unfilteredItems.Count == 0)
        {
            Close();
            return;
        }

        bool showSearch = CanSearchOpenMenu();
        bool opensUp = showSearch && direction == ChatActionMenuDirection.Up;
        IReadOnlyList<ChatActionMenuItem> items = GetFilteredMenuItems(unfilteredItems, showSearch);
        int visibleRows = Math.Min(Math.Max(1, items.Count), MaximumVisibleMenuRows);
        int headerHeight = string.IsNullOrEmpty(title) ? 0 : MenuRowHeight;
        Rectangle bounds = GetMenuBounds(items.Count, title, desiredX, y, buttonSize, showSearch, direction);
        Utils.DrawInvBG(Main.spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, new Color(34, 42, 58, 224));

        int maximumScroll = Math.Max(0, items.Count - visibleRows);
        TryConsumeMenuScroll(items.Count, bounds);
        menuScroll = Math.Max(0, Math.Min(maximumScroll, menuScroll));
        int contentTop = bounds.Y + 5;
        if (headerHeight != 0)
        {
            var back = new Rectangle(bounds.X + 5, contentTop, bounds.Width - 10, MenuRowHeight - 1);
            bool backHovered = back.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
            if (backHovered)
            {
                Main.LocalPlayer.mouseInterface = true;
                Utils.DrawInvBG(Main.spriteBatch, back.X, back.Y, back.Width, back.Height, new Color(68, 91, 121, 220));
                if (pressed && !consumed)
                {
                    openMenuPath.RemoveAt(openMenuPath.Count - 1);
                    ResetMenuSearch();
                    ResetMenuAnchor();
                    consumed = true;
                    Main.mouseLeftRelease = false;
                    SoundEngine.PlaySound(11);
                }
            }

            Utils.DrawBorderString(Main.spriteBatch, "< " + title, new Vector2(back.X + 8, back.Center.Y), Color.White, 0.63f, 0f, 0.5f, -1);
            contentTop += MenuRowHeight;
        }

        int optionsTop = contentTop;
        int searchTop = -1;
        if (showSearch)
        {
            searchTop = opensUp ? bounds.Bottom - MenuSearchHeight - 5 : contentTop;
            if (!opensUp)
            {
                optionsTop += MenuSearchHeight;
            }
        }

        DrawMenuScrollbar(bounds, optionsTop, visibleRows, items.Count);

        if (showSearch)
        {
            var search = new Rectangle(bounds.X + 5, searchTop, bounds.Width - 10, MenuSearchHeight - 1);
            bool searchHovered = search.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
            if (searchHovered || menuSearchFocused)
            {
                Main.LocalPlayer.mouseInterface = true;
                Utils.DrawInvBG(Main.spriteBatch, search.X, search.Y, search.Width, search.Height, new Color(58, 76, 105, 205));
            }

            DrawMenuSearchSelection(search, 0.66f);

            string searchText = string.IsNullOrEmpty(menuSearch) ? "Search..." : "Search: " + menuSearch;
            if (menuSearchFocused && Main.instance != null && Main.instance.textBlinkerState == 1)
            {
                searchText = InsertSearchCaret(searchText, menuSearchBuffer.Caret);
            }

            Utils.DrawBorderString(Main.spriteBatch, searchText, new Vector2(search.X + 8, search.Center.Y), string.IsNullOrEmpty(menuSearch) ? Color.Gray : Color.White, menuSearchFocused || searchHovered ? 0.66f : 0.62f, 0f, 0.5f, -1);
            if (searchHovered && pressed && !consumed)
            {
                menuSearchFocused = true;
                Main.clrInput();
                consumed = true;
                Main.mouseLeftRelease = false;
                SoundEngine.PlaySound(12);
            }
        }

        string currentHoveredMenuItemId = null;
        for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
        {
            int index = menuScroll + rowIndex;
            if (index >= items.Count)
            {
                break;
            }

            ChatActionMenuItem item = items[index];
            if (item == null)
            {
                continue;
            }

            var row = new Rectangle(bounds.X + 5, optionsTop + rowIndex * MenuRowHeight, bounds.Width - 10, MenuRowHeight - 1);
            bool hovered = row.Contains(Main.mouseX, Main.mouseY) && !PlayerInput.IgnoreMouseInterface;
            if (hovered)
            {
                Main.LocalPlayer.mouseInterface = true;
                if (item.Enabled)
                {
                    currentHoveredMenuItemId = item.Id;
                }

                Utils.DrawInvBG(Main.spriteBatch, row.X, row.Y, row.Width, row.Height, item.Enabled ? new Color(68, 91, 121, 220) : new Color(54, 54, 54, 180));
            }

            Color textColor = item.Enabled ? Color.White : Color.Gray;
            Utils.DrawBorderString(Main.spriteBatch, item.Label, new Vector2(row.X + 8, row.Center.Y), textColor, 0.63f, 0f, 0.5f, -1);
            if (!string.IsNullOrEmpty(item.Value))
            {
                Utils.DrawBorderString(Main.spriteBatch, item.Value, new Vector2(row.Right - 8, row.Center.Y), textColor, 0.63f, 1f, 0.5f, -1);
            }
            else if (item.HasChildren)
            {
                Utils.DrawBorderString(Main.spriteBatch, ">", new Vector2(row.Right - 8, row.Center.Y), textColor, 0.63f, 1f, 0.5f, -1);
            }

            if (!pressed || !hovered || consumed || !item.Enabled)
            {
                continue;
            }

            if (item.HasChildren)
            {
                openMenuPath.Add(item.Id);
                ResetMenuSearch();
                ResetMenuAnchor();
                consumed = true;
                Main.mouseLeftRelease = false;
                SoundEngine.PlaySound(10);
            }
            else if (chat.TryActivateActionButtonMenuItem(openOwner, openButtonId, item.Id))
            {
                // A leaf selection returns to its parent chooser so related settings can be changed
                // without reopening the action strip.
                if (openMenuPath.Count != 0)
                {
                    openMenuPath.RemoveAt(openMenuPath.Count - 1);
                }
                ResetMenuSearch();
                ResetMenuAnchor();
                consumed = true;
                Main.mouseLeftRelease = false;
                SoundEngine.PlaySound(12);
            }
        }

        UpdateMenuItemHoverSound(currentHoveredMenuItemId);

        if (items.Count == 0)
        {
            Utils.DrawBorderString(Main.spriteBatch, "No matching choices.", new Vector2(bounds.X + 13, optionsTop + MenuRowHeight / 2), Color.Gray, 0.63f, 0f, 0.5f, -1);
        }

        if (pressed && !consumed && !bounds.Contains(Main.mouseX, Main.mouseY))
        {
            Close();
            SoundEngine.PlaySound(11);
        }
    }

    private static int GetActionButtonX(int buttonCount)
    {
        return 78 - 6 - ChatActionButtonHeight - (buttonCount - 1) * (ChatActionButtonHeight + ButtonSpacing);
    }

    private static int GetActionButtonY()
    {
        return Main.screenHeight - ChatInputHeight + ChatActionButtonBottomInset;
    }

    private static Rectangle GetMenuBounds(int itemCount, string title, int desiredX, int y, int buttonSize, bool showSearch, ChatActionMenuDirection direction)
    {
        int visibleRows = Math.Min(Math.Max(1, itemCount), MaximumVisibleMenuRows);
        int headerHeight = string.IsNullOrEmpty(title) ? 0 : MenuRowHeight;
        int height = visibleRows * MenuRowHeight + headerHeight + (showSearch ? MenuSearchHeight : 0) + 10;
        int menuX = Math.Max(8, desiredX);
        if (showSearch && direction == ChatActionMenuDirection.Up)
        {
            int desiredBottom = Math.Min(Main.screenHeight - 8, y + buttonSize);
            if (menuBottom < 0 || menuTopScreenHeight != Main.screenHeight)
            {
                menuBottom = Math.Max(8 + height, desiredBottom);
                menuTopScreenHeight = Main.screenHeight;
            }

            return new Rectangle(menuX, Math.Max(8, menuBottom - height), MenuWidth, height);
        }

        int desiredTop = Math.Max(8, y - height + buttonSize);
        if (menuTop < 0 || menuTopScreenHeight != Main.screenHeight)
        {
            menuTop = desiredTop;
            menuTopScreenHeight = Main.screenHeight;
        }

        // A filtered menu retains the original top edge and contracts only at the bottom.
        return new Rectangle(menuX, menuTop, MenuWidth, height);
    }

    private static bool TryConsumeMenuScroll(int itemCount, Rectangle bounds)
    {
        if (!bounds.Contains(Main.mouseX, Main.mouseY))
        {
            return false;
        }

        int delta = PlayerInput.ScrollWheelDelta;
        if (delta == 0)
        {
            return false;
        }

        int steps = delta / 120;
        if (steps == 0)
        {
            steps = Math.Sign(delta);
        }

        int maximumScroll = Math.Max(0, itemCount - Math.Min(itemCount, MaximumVisibleMenuRows));
        menuScroll = Math.Max(0, Math.Min(maximumScroll, menuScroll - steps));
        PlayerInput.ScrollWheelDelta = 0;
        PlayerInput.ScrollWheelDeltaForUI = 0;
        Main.LocalPlayer.mouseInterface = true;
        return true;
    }

    private static void DrawMenuScrollbar(Rectangle bounds, int optionsTop, int visibleRows, int itemCount)
    {
        if (itemCount <= visibleRows)
        {
            return;
        }

        Texture2D pixel = PluginUiRuntime.GetIngameBlankTexture(Main.spriteBatch);
        if (pixel == null)
        {
            return;
        }

        int trackHeight = visibleRows * MenuRowHeight - 4;
        int trackX = bounds.Right - 8;
        int trackY = optionsTop + 2;
        var track = new Rectangle(trackX, trackY, 3, trackHeight);
        int thumbHeight = Math.Max(18, trackHeight * visibleRows / itemCount);
        int maximumScroll = itemCount - visibleRows;
        int thumbY = trackY + (trackHeight - thumbHeight) * menuScroll / maximumScroll;
        Main.spriteBatch.Draw(pixel, track, new Color(13, 18, 28, 210));
        Main.spriteBatch.Draw(pixel, new Rectangle(trackX - 1, thumbY, 5, thumbHeight), new Color(148, 168, 204, 235));
    }

    private static void DrawButton(SpriteBatch spriteBatch, Rectangle bounds, ChatActionButtonView view, bool hovered, ChatActionButtonVisualState visual)
    {
        DrawBackground(spriteBatch, bounds, hovered, visual);

        Texture2D texture = TryGetTexture(view.Owner, view.Descriptor.AssetPath);
        if (texture == null)
        {
            Utils.DrawBorderString(spriteBatch, "?", new Vector2(bounds.Center.X, bounds.Center.Y), Color.White, 0.65f, 0.5f, 0.5f, -1);
            return;
        }

        int padding = Math.Max(3, bounds.Width / 6);
        var destination = new Rectangle(bounds.X + padding, bounds.Y + padding, bounds.Width - padding * 2, bounds.Height - padding * 2);
        spriteBatch.Draw(texture, destination, Color.White);
    }

    private static void DrawBackground(SpriteBatch spriteBatch, Rectangle bounds, bool hovered, ChatActionButtonVisualState state)
    {
        Color fallback = hovered ? new Color(83, 103, 132, 230) : new Color(48, 57, 72, 210);
        if (!state.PrimaryBackground.HasValue)
        {
            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, fallback);
            return;
        }

        Color primary = ToColor(state.PrimaryBackground.Value);
        Color secondary = state.SecondaryBackground.HasValue ? ToColor(state.SecondaryBackground.Value) : primary;

        if (!state.SecondaryBackground.HasValue)
        {
            if (hovered)
            {
                primary = Color.Lerp(primary, Color.White, 0.12f);
            }

            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, primary * 0.9f);
            return;
        }

        DrawDualToneInventoryBackground(spriteBatch, bounds, primary * 0.9f, secondary * 0.9f, hovered);
    }

    // Preserve Terraria's nine-slice inventory-button silhouette while splitting the two active
    // translation modes along one hard diagonal. The source texture cannot be stretched as one
    // rectangle: that bypasses the native corner slices and visibly turns the button into a box.
    private static void DrawDualToneInventoryBackground(SpriteBatch spriteBatch, Rectangle bounds, Color primary, Color secondary, bool hovered)
    {
        Texture2D texture = PluginUiRuntime.RequestApprovedTexture("Images/Inventory_Back13");
        if (texture == null)
        {
            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, primary);
            return;
        }

        Texture2D background = GetDualToneBackground(texture, primary, secondary);
        if (background == null)
        {
            Utils.DrawInvBG(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, primary);
            return;
        }

        spriteBatch.Draw(background, bounds, Color.White);
    }

    private static Texture2D GetDualToneBackground(Texture2D source, Color primary, Color secondary)
    {
        if (dualToneBackgroundDevice != source.GraphicsDevice ||
            !dualToneBackgroundColorsSet ||
            dualTonePrimary != primary ||
            dualToneSecondary != secondary)
        {
            DisposeDualToneBackgrounds();
            try
            {
                dualToneBackgroundDevice = source.GraphicsDevice;
                dualTonePrimary = primary;
                dualToneSecondary = secondary;
                dualToneBackground = CreateDualToneBackground(source, primary, secondary);
                dualToneBackgroundColorsSet = dualToneBackground != null;
            }
            catch
            {
                DisposeDualToneBackgrounds();
            }
        }

        return dualToneBackground;
    }

    private static Texture2D CreateDualToneBackground(Texture2D source, Color primary, Color secondary)
    {
        const int cornerSize = 10;
        int width = ChatActionButtonHeight;
        int height = ChatActionButtonHeight;
        if (source.Width < cornerSize * 2 || source.Height < cornerSize * 2)
        {
            return null;
        }

        Color[] sourcePixels = GetDualToneSourcePixels(source);
        if (sourcePixels == null)
        {
            return null;
        }

        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            int sourceY = GetNineSliceCoordinate(y, height, source.Height, cornerSize);
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color tint = x + y < width ? primary : secondary;
                int sourceX = GetNineSliceCoordinate(x, width, source.Width, cornerSize);
                pixels[index] = TintSourcePixel(sourcePixels[sourceY * source.Width + sourceX], tint);
            }
        }

        var result = new Texture2D(source.GraphicsDevice, width, height, false, SurfaceFormat.Color);
        result.SetData(pixels);
        return result;
    }

    private static int GetNineSliceCoordinate(int value, int targetLength, int sourceLength, int cornerSize)
    {
        if (value < cornerSize)
        {
            return value;
        }

        if (value >= targetLength - cornerSize)
        {
            return sourceLength - cornerSize + value - (targetLength - cornerSize);
        }

        int targetCenterLength = targetLength - cornerSize * 2;
        // Utils.DrawInvBG stretches the ten-pixel center slice at (10, 0), rather than the
        // complete middle of Inventory_Back13. Mirror that exact source mapping here.
        return cornerSize + (value - cornerSize) * cornerSize / targetCenterLength;
    }

    private static Color[] GetDualToneSourcePixels(Texture2D source)
    {
        if (ReferenceEquals(dualToneSource, source) && dualToneSourcePixels != null)
        {
            return dualToneSourcePixels;
        }

        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        dualToneSource = source;
        dualToneSourcePixels = pixels;
        return pixels;
    }

    private static Color TintSourcePixel(Color source, Color tint)
    {
        return new Color(
            source.R * tint.R / byte.MaxValue,
            source.G * tint.G / byte.MaxValue,
            source.B * tint.B / byte.MaxValue,
            source.A * tint.A / byte.MaxValue);
    }

    private static void DisposeDualToneBackgrounds()
    {
        dualToneBackground?.Dispose();
        dualToneBackground = null;
        dualToneSource = null;
        dualToneSourcePixels = null;
        dualToneBackgroundDevice = null;
        dualToneBackgroundColorsSet = false;
    }

    private static IReadOnlyList<ChatActionMenuItem> ResolveOpenMenuItems(IReadOnlyList<ChatActionMenuItem> root, out string title, out ChatActionMenuDirection direction)
    {
        IReadOnlyList<ChatActionMenuItem> current = root;
        title = string.Empty;
        direction = ChatActionMenuDirection.Down;
        for (int depth = 0; depth < openMenuPath.Count; depth++)
        {
            if (!TryFindMenuItem(current, openMenuPath[depth], out ChatActionMenuItem item) || !item.HasChildren)
            {
                openMenuPath.RemoveRange(depth, openMenuPath.Count - depth);
                ResetMenuSearch();
                ResetMenuAnchor();
                break;
            }

            current = item.Children;
            title = item.Label;
            direction = item.ChildMenuDirection;
        }

        return current;
    }

    private static IReadOnlyList<ChatActionMenuItem> GetFilteredMenuItems(IReadOnlyList<ChatActionMenuItem> source, bool allowSearch)
    {
        if (!allowSearch || string.IsNullOrWhiteSpace(menuSearch))
        {
            return source;
        }

        filteredMenuItems.Clear();
        for (int index = 0; index < source.Count; index++)
        {
            ChatActionMenuItem item = source[index];
            if (item != null && PluginDropdownFilter.Matches(item.Label, item.Value, menuSearch))
            {
                filteredMenuItems.Add(item);
            }
        }

        return filteredMenuItems;
    }

    private static bool TryFindMenuItem(IReadOnlyList<ChatActionMenuItem> items, string id, out ChatActionMenuItem item)
    {
        for (int index = 0; index < items.Count; index++)
        {
            ChatActionMenuItem candidate = items[index];
            if (candidate != null && string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                item = candidate;
                return true;
            }
        }

        item = null;
        return false;
    }

    private static void UpdateButtonHoverSound(PluginId owner, string id)
    {
        bool changed = owner != hoveredOwner || !string.Equals(id, hoveredButtonId, StringComparison.Ordinal);
        hoveredOwner = owner;
        hoveredButtonId = id;
        if (changed && owner.IsValid)
        {
            SoundEngine.PlaySound(12);
        }
    }

    private static void UpdateMenuItemHoverSound(string id)
    {
        bool changed = !string.Equals(id, hoveredMenuItemId, StringComparison.Ordinal);
        hoveredMenuItemId = id;
        if (changed && !string.IsNullOrEmpty(id))
        {
            SoundEngine.PlaySound(12);
        }
    }

    private static Texture2D TryGetTexture(PluginId owner, string assetPath)
    {
        var key = new ChatActionTextureKey(owner, assetPath);
        if (textures.TryGetValue(key, out Texture2D texture))
        {
            return texture;
        }

        if (missingTextures.Contains(key))
        {
            return null;
        }

        try
        {
            if (content == null)
            {
                content = new ContentManager(Main.instance.Services, AppDomain.CurrentDomain.BaseDirectory);
            }

            texture = content.Load<Texture2D>("plugins/" + owner.Value + "/" + assetPath);
            textures.Add(key, texture);
            return texture;
        }
        catch
        {
            missingTextures.Add(key);
            return null;
        }
    }

    private static Color ToColor(PluginColor color)
    {
        return new Color(color.Red, color.Green, color.Blue);
    }

    private static void Close()
    {
        openOwner = default;
        openButtonId = string.Empty;
        openMenuPath.Clear();
        ResetMenuSearch();
        ResetMenuAnchor();
        hoveredMenuItemId = null;
    }

    private static void ResetMenuSearch()
    {
        menuScroll = 0;
        menuSearch = string.Empty;
        menuSearchBuffer.Clear();
        menuSearchBackspaceRepeat = default;
        menuSearchDeleteRepeat = default;
        menuSearchLeftRepeat = default;
        menuSearchRightRepeat = default;
        menuSearchFocused = false;
        filteredMenuItems.Clear();
    }

    private static void ProcessMenuSearchKeys(KeyboardState current, KeyboardState previous)
    {
        bool control = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        if (control && Pressed(current, previous, Keys.A))
        {
            menuSearchBuffer.SelectAll();
        }
        else if (menuSearchBackspaceRepeat.ShouldRepeat(current, previous, Keys.Back))
        {
            menuSearchBuffer.Backspace(control);
        }
        else if (menuSearchDeleteRepeat.ShouldRepeat(current, previous, Keys.Delete))
        {
            menuSearchBuffer.Delete(control);
        }
        else if (menuSearchLeftRepeat.ShouldRepeat(current, previous, Keys.Left))
        {
            menuSearchBuffer.MoveLeft(control, shift);
        }
        else if (menuSearchRightRepeat.ShouldRepeat(current, previous, Keys.Right))
        {
            menuSearchBuffer.MoveRight(control, shift);
        }
        else if (Pressed(current, previous, Keys.Home))
        {
            menuSearchBuffer.MoveHome(shift);
        }
        else if (Pressed(current, previous, Keys.End))
        {
            menuSearchBuffer.MoveEnd(shift);
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
                    menuSearchBuffer.Insert(value);
                }
            }
        }

        menuScroll = 0;
    }

    private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }

    private static bool ShouldStartMenuSearch(KeyboardState current, KeyboardState previous)
    {
        // Releasing Escape changes the keyboard-state snapshot one frame after the key was
        // consumed. That transition is navigation, not a request to reopen search.
        if (IsMenuEscapePressed(current, previous) ||
            current.IsKeyDown(Keys.Escape) ||
            previous.IsKeyDown(Keys.Escape) ||
            Pressed(current, previous, Keys.Enter))
        {
            return false;
        }

        return HasPendingMenuSearchText();
    }

    private static bool HasPendingMenuSearchText()
    {
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

    // Player chat reports Escape through Main.keyInt on some native input paths while leaving
    // Main.inputText unchanged. Treat that raw event as navigation before generic key detection
    // can reopen the host search field.
    private static bool IsMenuEscapePressed(KeyboardState current, KeyboardState previous)
    {
        if (Pressed(current, previous, Keys.Escape))
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

    private static void DrawMenuSearchSelection(Rectangle search, float scale)
    {
        if (!menuSearchFocused ||
            !menuSearchBuffer.TryGetSelection(out int start, out int end))
        {
            return;
        }

        Texture2D pixel = PluginUiRuntime.RequestApprovedTexture("Images/MagicPixel");
        if (pixel == null)
        {
            return;
        }

        string value = menuSearchBuffer.Text;
        Vector2 left = Utils.DrawBorderString(Main.spriteBatch, "Search: " + value.Substring(0, start), Vector2.Zero, Color.Transparent, scale);
        Vector2 right = Utils.DrawBorderString(Main.spriteBatch, "Search: " + value.Substring(0, end), Vector2.Zero, Color.Transparent, scale);
        int width = Math.Max(1, (int)Math.Ceiling(right.X - left.X));
        int height = Math.Max(1, (int)Math.Ceiling(Utils.DrawBorderString(Main.spriteBatch, " ", Vector2.Zero, Color.Transparent, scale).Y));
        var selection = new Rectangle(search.X + 8 + (int)left.X, search.Center.Y - height / 2, width, height);
        Main.spriteBatch.Draw(pixel, selection, new Color(96, 142, 218, 150));
    }

    private static void ResetMenuAnchor()
    {
        menuTop = -1;
        menuBottom = -1;
        menuTopScreenHeight = -1;
    }

    private static bool CanSearchOpenMenu()
    {
        // Root action popovers are command menus. Nested child lists are actual dropdown
        // choosers, so only they expose host-owned text filtering.
        return openMenuPath.Count != 0;
    }

    private readonly struct ChatActionTextureKey : IEquatable<ChatActionTextureKey>
    {
        internal ChatActionTextureKey(PluginId owner, string assetPath)
        {
            Owner = owner;
            AssetPath = assetPath ?? string.Empty;
        }

        private PluginId Owner { get; }
        private string AssetPath { get; }

        public bool Equals(ChatActionTextureKey other)
        {
            return Owner == other.Owner && string.Equals(AssetPath, other.AssetPath, StringComparison.Ordinal);
        }

        public override bool Equals(object value)
        {
            return value is ChatActionTextureKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Owner.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(AssetPath));
            }
        }
    }
}
