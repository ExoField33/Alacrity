using System;
using System.Reflection;
using AlacrityTerraria.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;

namespace AlacrityTerraria;

/// <summary>
/// Version-locked native text input forwarding. Terraria funnels ordinary desktop text fields
/// through <see cref="Main.GetInputText(string, bool)"/>; this implementation modernizes that
/// one helper without exposing native input state to plugins.
/// </summary>
public static partial class PluginUiRuntime
{
    private const int TextRepeatDelayMilliseconds = 320;
    private const int TextRepeatIntervalMilliseconds = 38;
    private static readonly NativeTextEditState nativeTextEditState = new NativeTextEditState();
    private static TextKeyRepeatState leftRepeatState;
    private static TextKeyRepeatState rightRepeatState;
    private static TextKeyRepeatState upRepeatState;
    private static TextKeyRepeatState downRepeatState;
    private static TextKeyRepeatState backspaceRepeatState;
    private static TextKeyRepeatState deleteRepeatState;
    private static bool escapeInputArmed;
    private static bool discardOpeningPlayerChatInput;
    private static bool nativeTextInputFailureReported;
    private static bool clipboardResolved;
    private static object clipboardService;
    private static PropertyInfo clipboardValue;
    private static bool imeResolved;
    private static object imeService;
    private static PropertyInfo imeCompositionString;
    private static FieldInfo imeCompositionActive;
    private static bool textMeasurementResolved;
    private static object mouseTextFont;
    private static MethodInfo chatTextMeasure;
    private static Type textBoxFontType;
    private static MethodInfo textBoxMeasure;
    private static bool selectionPixelResolved;
    private static Texture2D selectionPixel;

    /// <summary>
    /// Attempts to process a focused native Terraria text field. Returning <see langword="false"/>
    /// leaves the original version-locked method body in control, preserving vanilla behavior if
    /// the bridge cannot safely process the current input state.
    /// </summary>
    public static bool TryProcessNativeTextInput(string oldText, bool allowMultiLine, out string result)
    {
        result = oldText ?? string.Empty;
        if (Main.dedServ || !FocusHelper.AllowUIInputs || IsImeCompositionActive())
        {
            return false;
        }

        try
        {
            if (Main.drawingPlayerChat && discardOpeningPlayerChatInput)
            {
                // OpenPlayerChat sets drawingPlayerChat before calling clrInput.  A key event
                // already queued by that same transition must not become a submit in the first
                // GetInputText invocation.
                discardOpeningPlayerChatInput = false;
                Main.keyCount = 0;
                Main.inputTextEnter = false;
                Main.inputTextEscape = false;
                Main.oldInputText = Main.inputText;
                Main.inputText = Keyboard.GetState();
                result = oldText ?? string.Empty;
                return true;
            }

            // Player chat already has an activation-scoped editor when BetterChat is active.
            // Let that one owner process both edits and presentation; mixing its edit state with
            // the generic native state leaves the visible caret detached from the actual edit.
            if (Main.drawingPlayerChat && HasChatInputEditors())
            {
                result = ProcessChatInput(result, allowMultiLine);
                return true;
            }

            result = ProcessNativeTextInput(result, allowMultiLine);
            return true;
        }
        catch (Exception exception)
        {
            ResetNativeTextInput();
            if (!nativeTextInputFailureReported)
            {
                nativeTextInputFailureReported = true;
                RecordFailure("Process native text input", exception);
            }

            result = oldText ?? string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Returns the retained caret for an active native text field. Version-locked UI text boxes
    /// use this only for presentation; their stored text stays unmodified.
    /// </summary>
    public static int GetNativeTextInputCaret(string text)
    {
        try
        {
            return nativeTextEditState.GetCaret(text);
        }
        catch
        {
            return (text ?? string.Empty).Length;
        }
    }

    /// <summary>
    /// Formats legacy menu input text for presentation only. Those menu screens append their
    /// own ticker to a detached display string instead of drawing a UITextBox.
    /// </summary>
    public static string FormatNativeTextInputDisplay(string text)
    {
        try
        {
            return nativeTextEditState.FormatNativeDisplayText(text);
        }
        catch
        {
            return text ?? string.Empty;
        }
    }

    /// <summary>
    /// Draws the selected player-chat range before Terraria draws its normal chat snippets. The
    /// editable text stays untouched, so outgoing packets and Terraria tag parsing remain native.
    /// </summary>
    public static void DrawNativePlayerChatSelection(SpriteBatch spriteBatch, string text)
    {
        if (spriteBatch == null || HasChatInputEditors() || !nativeTextEditState.TryGetFocusedPresentation(text, out _, out int start, out int end) || start == end)
        {
            return;
        }

        try
        {
            float left = MeasureChatText(text.Substring(0, start));
            float right = MeasureChatText(text.Substring(0, end));
            DrawSelection(spriteBatch, 88f + left, Main.screenHeight - 31f, right - left, 18f);
        }
        catch
        {
            // A malformed native tag or unavailable asset must leave Terraria's chat draw intact.
        }
    }

    /// <summary>
    /// Draws a native <c>UITextBox</c> selection without retaining the native UI object. It runs
    /// after the normal textbox draw so the existing panel/layout code remains authoritative.
    /// </summary>
    public static void DrawNativeTextBoxSelection(SpriteBatch spriteBatch, string text, Vector2 textPosition, object font, float textScale)
    {
        if (spriteBatch == null || font == null || !nativeTextEditState.TryGetPresentation(text, out _, out int start, out int end) || start == end)
        {
            return;
        }

        try
        {
            float left = MeasureTextBox(font, text.Substring(0, start)).X * textScale;
            float right = MeasureTextBox(font, text.Substring(0, end)).X * textScale;
            float height = MeasureTextBox(font, " ").Y * textScale;
            DrawSelection(spriteBatch, textPosition.X + left, textPosition.Y, right - left, height);
        }
        catch
        {
            // Optional presentation cannot prevent the native field from rendering.
        }
    }

    /// <summary>Clears caret, selection, and key-repeat state when Terraria activates a new text field.</summary>
    public static void ResetNativeTextInput()
    {
        nativeTextEditState.Reset();
        leftRepeatState = default;
        rightRepeatState = default;
        upRepeatState = default;
        downRepeatState = default;
        backspaceRepeatState = default;
        deleteRepeatState = default;
        escapeInputArmed = false;
        discardOpeningPlayerChatInput = Main.drawingPlayerChat;
    }

    /// <summary>Formats editable player chat with the current core-owned caret and selection.</summary>
    private static string FormatNativePlayerChatText(string text)
    {
        try
        {
            bool drawCaret = Main.instance != null && Main.instance.textBlinkerState == 1;
            return nativeTextEditState.FormatForPlayerChat(text, drawCaret);
        }
        catch (Exception exception)
        {
            if (!nativeTextInputFailureReported)
            {
                nativeTextInputFailureReported = true;
                RecordFailure("Format native player chat text", exception);
            }

            return Main.instance != null && Main.instance.textBlinkerState == 1
                ? (text ?? string.Empty) + "|"
                : text;
        }
    }

    private static string ProcessNativeTextInput(string text, bool allowMultiLine)
    {
        nativeTextEditState.Synchronize(text);

        // An asynchronous outgoing chat transformation may have made a completed result ready
        // between input frames. Preserve only this one acknowledged player-chat submit through
        // the helper's normal flag reset so Terraria sends it without another keystroke.
        bool pendingChatSubmit = Main.drawingPlayerChat && Main.inputTextEnter && Main.chatRelease;
        Main.inputTextEnter = false;
        Main.inputTextEscape = false;

        KeyboardState current = Main.inputText;
        KeyboardState previous = Main.oldInputText;
        bool control = current.IsKeyDown(Keys.LeftControl) || current.IsKeyDown(Keys.RightControl);
        bool shift = current.IsKeyDown(Keys.LeftShift) || current.IsKeyDown(Keys.RightShift);
        bool alternate = current.IsKeyDown(Keys.LeftAlt) || current.IsKeyDown(Keys.RightAlt);

        if (Main.drawingPlayerChat && TryProcessChatActionInput())
        {
            Main.inputTextEnter = pendingChatSubmit;
            Main.inputTextEscape = false;
            Main.keyCount = 0;
            Main.oldInputText = current;
            Main.inputText = Keyboard.GetState();
            UpdateImeCompositionState();
            nativeTextEditState.Complete(text);
            return text;
        }

        if (control && !alternate)
        {
            ProcessControlShortcut(ref text, allowMultiLine, current, previous);
        }
        else
        {
            ProcessShiftClipboardShortcut(ref text, allowMultiLine, shift, current, previous);
            ProcessTypedCharacters(ref text, allowMultiLine);
        }

        // Match Terraria's native ordering: text events consume the previous poll, then the
        // fresh keyboard snapshot drives held-key editing. Checking the stale snapshot made
        // repeated navigation and deletion dependent on unrelated input calls.
        Main.keyCount = 0;
        Main.oldInputText = current;
        Main.inputText = Keyboard.GetState();
        UpdateImeCompositionState();

        KeyboardState navigationCurrent = Main.inputText;
        KeyboardState navigationPrevious = Main.oldInputText;
        control = navigationCurrent.IsKeyDown(Keys.LeftControl) || navigationCurrent.IsKeyDown(Keys.RightControl);
        shift = navigationCurrent.IsKeyDown(Keys.LeftShift) || navigationCurrent.IsKeyDown(Keys.RightShift);

        if (Repeated(navigationCurrent, navigationPrevious, Keys.Left, ref leftRepeatState))
        {
            nativeTextEditState.MoveLeft(text, control, shift);
        }

        if (Repeated(navigationCurrent, navigationPrevious, Keys.Right, ref rightRepeatState))
        {
            nativeTextEditState.MoveRight(text, control, shift);
        }

        if (Pressed(navigationCurrent, navigationPrevious, Keys.Home))
        {
            nativeTextEditState.MoveHome(shift);
        }

        if (Pressed(navigationCurrent, navigationPrevious, Keys.End))
        {
            nativeTextEditState.MoveEnd(text, shift);
        }

        if (Repeated(navigationCurrent, navigationPrevious, Keys.Back, ref backspaceRepeatState))
        {
            text = nativeTextEditState.Backspace(text, control);
        }

        if (Repeated(navigationCurrent, navigationPrevious, Keys.Delete, ref deleteRepeatState))
        {
            text = nativeTextEditState.Delete(text, control);
        }

        if (!escapeInputArmed)
        {
            escapeInputArmed = !navigationCurrent.IsKeyDown(Keys.Escape);
        }
        else if (Pressed(navigationCurrent, navigationPrevious, Keys.Escape))
        {
            Main.inputTextEscape = !Main.drawingPlayerChat || !TryHandleChatActionEscape();
        }

        ProcessGenericChatActions(ref text, control, shift);

        if (pendingChatSubmit)
        {
            Main.inputTextEnter = true;
        }

        nativeTextEditState.Complete(text);
        return text;
    }

    private static void ProcessGenericChatActions(ref string text, bool control, bool shift)
    {
        if (!Main.drawingPlayerChat)
        {
            return;
        }

        KeyboardState current = Main.keyState;
        KeyboardState previous = Main.oldKeyState;
        if (Repeated(current, previous, Keys.Up, ref upRepeatState))
        {
            ApplyGenericChatAction(ref text, "up", control, shift, 0);
        }

        if (Repeated(current, previous, Keys.Down, ref downRepeatState))
        {
            ApplyGenericChatAction(ref text, "down", control, shift, 0);
        }

        int scrollLines = Terraria.GameInput.PlayerInput.ScrollWheelDelta / 120;
        if (scrollLines != 0 && ApplyGenericChatAction(ref text, "scroll", control, shift, scrollLines))
        {
            Terraria.GameInput.PlayerInput.ScrollWheelDelta = 0;
            Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI = 0;
        }
    }

    private static bool ApplyGenericChatAction(ref string text, string actionId, bool control, bool shift, int scrollLines)
    {
        if (!TryApplyChatInputAction(
                text,
                nativeTextEditState.Caret,
                nativeTextEditState.SelectionAnchor,
                actionId,
                control,
                shift,
                scrollLines,
                out string resultText,
                out int resultCaret,
                out int resultSelectionAnchor,
                out _))
        {
            return false;
        }

        text = resultText ?? string.Empty;
        nativeTextEditState.Replace(text, resultCaret, resultSelectionAnchor);
        return true;
    }

    private static void ProcessControlShortcut(ref string text, bool allowMultiLine, KeyboardState current, KeyboardState previous)
    {
        if (Pressed(current, previous, Keys.A))
        {
            nativeTextEditState.SelectAll(text);
            return;
        }

        if (Pressed(current, previous, Keys.Z))
        {
            nativeTextEditState.SelectAll(text);
            text = nativeTextEditState.Delete(text, byWord: false);
            return;
        }

        if (Pressed(current, previous, Keys.C) || Pressed(current, previous, Keys.Insert))
        {
            TryWriteNativeClipboard(nativeTextEditState.SelectedOrAll(text));
            return;
        }

        if (Pressed(current, previous, Keys.X))
        {
            TryWriteNativeClipboard(nativeTextEditState.SelectedOrAll(text));
            nativeTextEditState.SelectAll(text);
            text = nativeTextEditState.Delete(text, byWord: false);

            return;
        }

        if (Pressed(current, previous, Keys.V))
        {
            text = nativeTextEditState.Insert(text, NormalizeNativeText(TryReadNativeClipboard(), allowMultiLine));
        }
    }

    private static void ProcessShiftClipboardShortcut(ref string text, bool allowMultiLine, bool shift, KeyboardState current, KeyboardState previous)
    {
        if (!shift)
        {
            return;
        }

        if (Pressed(current, previous, Keys.Insert))
        {
            text = nativeTextEditState.Insert(text, NormalizeNativeText(TryReadNativeClipboard(), allowMultiLine));
            return;
        }

        if (Pressed(current, previous, Keys.Delete))
        {
            TryWriteNativeClipboard(nativeTextEditState.SelectedOrAll(text));
            nativeTextEditState.SelectAll(text);
            text = nativeTextEditState.Delete(text, byWord: false);
        }
    }

    private static void ProcessTypedCharacters(ref string text, bool allowMultiLine)
    {
        int count = Math.Max(0, Math.Min(Main.keyCount, Math.Min(Main.keyInt.Length, Main.keyString.Length)));
        for (int index = 0; index < count; index++)
        {
            int key = Main.keyInt[index];
            if (key == 13)
            {
                Main.inputTextEnter = true;
            }
            else if (key == 27)
            {
                Main.inputTextEscape = true;
            }
            else if (key >= 32 && key != 127)
            {
                text = nativeTextEditState.Insert(text, NormalizeNativeText(Main.keyString[index] ?? string.Empty, allowMultiLine));
            }
        }
    }

    private static bool Pressed(KeyboardState current, KeyboardState previous, Keys key)
    {
        return current.IsKeyDown(key) && !previous.IsKeyDown(key);
    }

    private static bool Repeated(KeyboardState current, KeyboardState previous, Keys key, ref TextKeyRepeatState state)
    {
        if (!current.IsKeyDown(key))
        {
            state = default;
            return false;
        }

        int now = Environment.TickCount;
        if (!previous.IsKeyDown(key) || !state.Held)
        {
            state.Held = true;
            state.StartTick = now;
            state.LastTick = now;
            return true;
        }

        if (Elapsed(now, state.StartTick) < TextRepeatDelayMilliseconds ||
            Elapsed(now, state.LastTick) < TextRepeatIntervalMilliseconds)
        {
            return false;
        }

        state.LastTick = now;
        return true;
    }

    private static string NormalizeNativeText(string value, bool allowMultiLine)
    {
        value = value ?? string.Empty;
        return allowMultiLine ? value : value.Replace("\r", string.Empty).Replace("\n", " ");
    }

    private static string TryReadNativeClipboard()
    {
        EnsureNativeClipboard();
        try
        {
            return clipboardValue == null || clipboardService == null
                ? string.Empty
                : clipboardValue.GetValue(clipboardService, null) as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryWriteNativeClipboard(string text)
    {
        EnsureNativeClipboard();
        try
        {
            clipboardValue?.SetValue(clipboardService, text ?? string.Empty, null);
        }
        catch
        {
            // Clipboard failure must not prevent normal text input from proceeding.
        }
    }

    private static void EnsureNativeClipboard()
    {
        if (clipboardResolved)
        {
            return;
        }

        clipboardResolved = true;
        try
        {
            Type platformType = Type.GetType("ReLogic.OS.Platform, ReLogic", throwOnError: false);
            Type clipboardType = Type.GetType("ReLogic.OS.IClipboard, ReLogic", throwOnError: false);
            MethodInfo get = platformType == null || clipboardType == null
                ? null
                : platformType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)?.MakeGenericMethod(clipboardType);
            clipboardService = get?.Invoke(null, null);
            clipboardValue = clipboardService?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            clipboardService = null;
            clipboardValue = null;
        }
    }

    private static bool IsImeCompositionActive()
    {
        EnsureNativeIme();
        try
        {
            return imeCompositionActive != null && imeCompositionActive.GetValue(null) is bool value && value;
        }
        catch
        {
            return false;
        }
    }

    private static void UpdateImeCompositionState()
    {
        EnsureNativeIme();
        try
        {
            if (imeCompositionActive != null)
            {
                string composition = imeCompositionString?.GetValue(imeService, null) as string;
                imeCompositionActive.SetValue(null, !string.IsNullOrEmpty(composition));
            }
        }
        catch
        {
            // IME availability is platform-dependent. Input remains usable when the platform
            // service cannot be reflected by this version-locked facade.
        }
    }

    private static void EnsureNativeIme()
    {
        if (imeResolved)
        {
            return;
        }

        imeResolved = true;
        try
        {
            Type platformType = Type.GetType("ReLogic.OS.Platform, ReLogic", throwOnError: false);
            Type imeType = Type.GetType("ReLogic.OS.IImeService, ReLogic", throwOnError: false);
            MethodInfo get = platformType == null || imeType == null
                ? null
                : platformType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)?.MakeGenericMethod(imeType);
            imeService = get?.Invoke(null, null);
            imeCompositionString = imeService?.GetType().GetProperty("CompositionString", BindingFlags.Public | BindingFlags.Instance);
            imeCompositionActive = typeof(Main).GetField("imeCompositionActive", BindingFlags.NonPublic | BindingFlags.Static);
        }
        catch
        {
            imeService = null;
            imeCompositionString = null;
            imeCompositionActive = null;
        }
    }

    private static int Elapsed(int current, int previous)
    {
        return unchecked(current - previous);
    }

    private static float MeasureChatText(string text)
    {
        EnsureTextMeasurement();
        if (mouseTextFont == null || chatTextMeasure == null)
        {
            throw new InvalidOperationException("Terraria chat text measurement is unavailable.");
        }

        object result = chatTextMeasure.Invoke(null, new object[] { mouseTextFont, text ?? string.Empty, Vector2.One, -1f });
        return result is Vector2 size ? size.X : 0f;
    }

    private static Vector2 MeasureTextBox(object font, string text)
    {
        if (font == null)
        {
            throw new ArgumentNullException(nameof(font));
        }

        Type type = font.GetType();
        if (textBoxFontType != type)
        {
            textBoxFontType = type;
            textBoxMeasure = type.GetMethod("MeasureString", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        }

        object result = textBoxMeasure?.Invoke(font, new object[] { text ?? string.Empty });
        return result is Vector2 size ? size : Vector2.Zero;
    }

    private static void EnsureTextMeasurement()
    {
        if (textMeasurementResolved)
        {
            return;
        }

        textMeasurementResolved = true;
        try
        {
            Assembly terraria = typeof(Main).Assembly;
            Type fontAssetsType = terraria.GetType("Terraria.GameContent.FontAssets", throwOnError: false);
            Type chatManagerType = terraria.GetType("Terraria.UI.Chat.ChatManager", throwOnError: false);
            FieldInfo mouseText = fontAssetsType?.GetField("MouseText", BindingFlags.Public | BindingFlags.Static);
            object asset = mouseText?.GetValue(null);
            mouseTextFont = asset?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(asset, null);
            if (chatManagerType == null || mouseTextFont == null)
            {
                return;
            }

            foreach (MethodInfo candidate in chatManagerType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name == "GetStringSize" && candidate.ReturnType == typeof(Vector2) && parameters.Length == 4 && parameters[1].ParameterType == typeof(string))
                {
                    chatTextMeasure = candidate;
                    return;
                }
            }
        }
        catch
        {
            mouseTextFont = null;
            chatTextMeasure = null;
        }
    }

    private static void DrawSelection(SpriteBatch spriteBatch, float x, float y, float width, float height)
    {
        if (width <= 0f || height <= 0f || !TryGetSelectionPixel(out Texture2D pixel))
        {
            return;
        }

        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        int right = (int)Math.Ceiling(x + width);
        int bottom = (int)Math.Ceiling(y + height);
        spriteBatch.Draw(
            pixel,
            new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)),
            new Color(80, 184, 255, 104));
    }

    private static bool TryGetSelectionPixel(out Texture2D pixel)
    {
        if (!selectionPixelResolved)
        {
            selectionPixelResolved = true;
            try
            {
                Type textureAssetsType = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets", throwOnError: false);
                object asset = textureAssetsType?.GetField("MagicPixel", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                selectionPixel = asset?.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(asset, null) as Texture2D;
            }
            catch
            {
                selectionPixel = null;
            }
        }

        pixel = selectionPixel;
        return pixel != null;
    }

    private struct TextKeyRepeatState
    {
        internal bool Held;
        internal int StartTick;
        internal int LastTick;
    }
}
