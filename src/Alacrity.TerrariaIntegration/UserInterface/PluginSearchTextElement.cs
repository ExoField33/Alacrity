using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.UI;

namespace AlacrityTerraria.UserInterface;

/// <summary>
/// A small host-owned search field for Alacrity menus. It deliberately does not route through
/// Terraria's append-only <c>UISearchBar</c>, whose version-locked textbox cursor cannot present
/// the host-owned caret and selection state consistently.
/// </summary>
internal sealed class PluginSearchTextElement : UIElement
{
    private readonly string placeholder;
    private readonly float textScale;
    private readonly PluginSearchTextBuffer buffer = new PluginSearchTextBuffer();
    private PluginSearchKeyRepeatState backspaceRepeat;
    private PluginSearchKeyRepeatState deleteRepeat;
    private PluginSearchKeyRepeatState leftRepeat;
    private PluginSearchKeyRepeatState rightRepeat;
    private bool isWritingText;

    internal PluginSearchTextElement(string placeholder, float textScale)
    {
        this.placeholder = placeholder ?? string.Empty;
        this.textScale = textScale;
        OnLeftClick += (_, __) => Focus();
    }

    internal event Action<string> ContentsChanged;

    internal bool IsWritingText => isWritingText;

    internal string Text => buffer.Text;

    internal void Focus(bool preservePendingText = false)
    {
        if (isWritingText)
        {
            return;
        }

        isWritingText = true;
        if (!preservePendingText)
        {
            Main.clrInput();
        }
    }

    internal void Blur()
    {
        if (!isWritingText)
        {
            return;
        }

        isWritingText = false;
        PlayerInput.WritingText = false;
        Main.instance?.HandleIME();
    }

    public override void Update(GameTime gameTime)
    {
        if (isWritingText)
        {
            PlayerInput.WritingText = true;
            Main.instance?.HandleIME();
            ProcessInput();
        }

        base.Update(gameTime);
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        base.DrawSelf(spriteBatch);
        CalculatedStyle dimensions = GetDimensions();
        string value = buffer.Text;
        string display = string.IsNullOrEmpty(value) ? placeholder : value;
        Color color = string.IsNullOrEmpty(value) ? Color.Gray : Color.White;
        Vector2 position = new Vector2(dimensions.X + 4f, dimensions.Y + dimensions.Height * 0.5f);

        DrawSelection(spriteBatch, position);
        Utils.DrawBorderString(spriteBatch, display, position, color, textScale, 0f, 0.5f, -1);
        DrawCaret(spriteBatch, position, value);
    }

    private void ProcessInput()
    {
        KeyboardState current = Main.inputText;
        KeyboardState previous = Main.oldInputText;
        bool control = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        string before = buffer.Text;

        if (control && Pressed(current, previous, Keys.A))
        {
            buffer.SelectAll();
        }
        else if (backspaceRepeat.ShouldRepeat(current, previous, Keys.Back))
        {
            buffer.Backspace(control);
        }
        else if (deleteRepeat.ShouldRepeat(current, previous, Keys.Delete))
        {
            buffer.Delete(control);
        }
        else if (leftRepeat.ShouldRepeat(current, previous, Keys.Left))
        {
            buffer.MoveLeft(control, shift);
        }
        else if (rightRepeat.ShouldRepeat(current, previous, Keys.Right))
        {
            buffer.MoveRight(control, shift);
        }
        else if (Pressed(current, previous, Keys.Home))
        {
            buffer.MoveHome(shift);
        }
        else if (Pressed(current, previous, Keys.End))
        {
            buffer.MoveEnd(shift);
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
                    buffer.Insert(value);
                }
            }
        }

        Main.keyCount = 0;
        Main.oldInputText = current;
        Main.inputText = Keyboard.GetState();

        if (!string.Equals(before, buffer.Text, StringComparison.Ordinal))
        {
            ContentsChanged?.Invoke(buffer.Text);
        }
    }

    private void DrawSelection(SpriteBatch spriteBatch, Vector2 position)
    {
        if (!isWritingText || !buffer.TryGetSelection(out int start, out int end))
        {
            return;
        }

        Texture2D pixel = PluginUiRuntime.RequestApprovedTexture("Images/MagicPixel");
        if (pixel == null)
        {
            return;
        }

        string value = buffer.Text;
        Vector2 left = Utils.DrawBorderString(spriteBatch, value.Substring(0, start), Vector2.Zero, Color.Transparent, textScale);
        Vector2 right = Utils.DrawBorderString(spriteBatch, value.Substring(0, end), Vector2.Zero, Color.Transparent, textScale);
        int width = Math.Max(1, (int)Math.Ceiling(right.X - left.X));
        int height = Math.Max(1, (int)Math.Ceiling(Utils.DrawBorderString(spriteBatch, " ", Vector2.Zero, Color.Transparent, textScale).Y));
        var selection = new Rectangle((int)position.X + (int)left.X, (int)position.Y - height / 2, width, height);
        spriteBatch.Draw(pixel, selection, new Color(96, 142, 218, 150));
    }

    // This custom UI field does not use Terraria's native text box. Render its own caret so the
    // plugin-manager search field is not coupled to Main.textBlinkerState's native input owner.
    private void DrawCaret(SpriteBatch spriteBatch, Vector2 position, string value)
    {
        if (!isWritingText)
        {
            return;
        }

        Texture2D pixel = PluginUiRuntime.RequestApprovedTexture("Images/MagicPixel");
        if (pixel == null)
        {
            return;
        }

        int caret = Math.Max(0, Math.Min(value.Length, buffer.Caret));
        Vector2 extent = Utils.DrawBorderString(spriteBatch, value.Substring(0, caret), Vector2.Zero, Color.Transparent, textScale);
        int height = Math.Max(2, (int)Math.Ceiling(Utils.DrawBorderString(spriteBatch, "|", Vector2.Zero, Color.Transparent, textScale).Y));
        var bounds = new Rectangle((int)position.X + (int)extent.X, (int)position.Y - height / 2, 2, height);
        spriteBatch.Draw(pixel, bounds, Color.White);
    }

    private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }
}
