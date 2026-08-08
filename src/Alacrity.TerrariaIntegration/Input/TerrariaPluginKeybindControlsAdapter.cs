using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Chat;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.UI;

namespace AlacrityTerraria.Input;

/// <summary>
/// Adds scoped plugin keybinds to Terraria's verified native controls UI. Reflection is resolved
/// once and a signature mismatch leaves the vanilla controls page untouched.
/// </summary>
internal sealed class TerrariaPluginKeybindControlsAdapter
{
    private readonly PluginExtensionHost extensions;
    private readonly Action<PluginKeybindRegistration, InputMode> ensureBinding;
    private readonly Action<PluginKeybindRegistration, InputMode, IReadOnlyList<string>> observeBinding;
    private readonly Action<string, Exception> reportFailure;
    private readonly ConditionalWeakTable<UIManageControls, ControlsState> states = new ConditionalWeakTable<UIManageControls, ControlsState>();
    private FieldInfo listField;
    private FieldInfo keyboardField;
    private FieldInfo gameplayField;

    internal TerrariaPluginKeybindControlsAdapter(PluginExtensionHost extensions, Action<PluginKeybindRegistration, InputMode> ensureBinding, Action<PluginKeybindRegistration, InputMode, IReadOnlyList<string>> observeBinding, Action<string, Exception> reportFailure)
    {
        this.extensions = extensions ?? throw new ArgumentNullException(nameof(extensions));
        this.ensureBinding = ensureBinding ?? throw new ArgumentNullException(nameof(ensureBinding));
        this.observeBinding = observeBinding ?? throw new ArgumentNullException(nameof(observeBinding));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
    }

    internal void Append(UIManageControls controls)
    {
        if (controls == null) return;
        try
        {
            if (!TryGetList(controls, out UIList list)) return;
            PluginKeybindRegistrySnapshot snapshot = extensions.GetKeybindSnapshot();
            ControlsState state = states.GetOrCreateValue(controls);
            if (state.Version == snapshot.Version) return;
            RemoveGroups(list);
            state.Version = snapshot.Version;
            if (snapshot.Registrations.Count == 0 || GetInputMode(controls) != InputMode.Keyboard) return;

            int groupOrder = 10000;
            int rowOrder = 20000;
            foreach (IGrouping<string, PluginKeybindRegistration> group in snapshot.Registrations.GroupBy(keybind => keybind.Owner.Value + "\u001f" + keybind.Heading, StringComparer.Ordinal))
                list.Add(CreateGroup(groupOrder++, ref rowOrder, group.First().Heading, group.ToArray(), InputMode.Keyboard));
        }
        catch (Exception exception) { reportFailure("Plugin controls-menu adapter", exception); }
    }

    private bool TryGetList(UIManageControls controls, out UIList list)
    {
        list = null;
        listField ??= typeof(UIManageControls).GetField("_uilist", BindingFlags.Instance | BindingFlags.NonPublic);
        if (listField == null || listField.FieldType != typeof(UIList)) throw new MissingFieldException(typeof(UIManageControls).FullName, "_uilist");
        list = listField.GetValue(controls) as UIList;
        return list != null;
    }

    private InputMode GetInputMode(UIManageControls controls)
    {
        keyboardField ??= typeof(UIManageControls).GetField("OnKeyboard", BindingFlags.Instance | BindingFlags.NonPublic);
        gameplayField ??= typeof(UIManageControls).GetField("OnGameplay", BindingFlags.Instance | BindingFlags.NonPublic);
        if (keyboardField?.FieldType != typeof(bool) || gameplayField?.FieldType != typeof(bool)) throw new MissingFieldException(typeof(UIManageControls).FullName, "OnKeyboard/OnGameplay");
        bool keyboard = (bool)keyboardField.GetValue(controls);
        bool gameplay = (bool)gameplayField.GetValue(controls);
        return keyboard ? (gameplay ? InputMode.Keyboard : InputMode.KeyboardUI) : (gameplay ? InputMode.XBoxGamepad : InputMode.XBoxGamepadUI);
    }

    private static void RemoveGroups(UIList list)
    {
        foreach (UIElement existing in list.Where(element => element is PluginKeybindControlGroup).ToArray()) list.Remove(existing);
    }

    private UIElement CreateGroup(int groupOrder, ref int rowOrder, string heading, IReadOnlyList<PluginKeybindRegistration> keybinds, InputMode mode)
    {
        var group = new PluginKeybindControlGroup(groupOrder) { HAlign = 0.5f, Width = StyleDimension.Fill, Height = new StyleDimension(2000f, 0f) };
        var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-16f, 1f), VAlign = 1f, BackgroundColor = Color.Lerp(new Color(33, 43, 79) * 0.8f, Color.MediumPurple, 0.18f) };
        group.Append(panel);
        var rows = new UIList { OverflowHidden = false, Width = StyleDimension.Fill, Height = new StyleDimension(-8f, 1f), VAlign = 1f, ListPadding = 5f };
        panel.Append(rows);
        foreach (PluginKeybindRegistration keybind in keybinds)
        {
            ensureBinding(keybind, mode);
            int order = rowOrder++;
            var row = new UISortableElement(order) { Width = StyleDimension.Fill, Height = new StyleDimension(30f, 0f), HAlign = 0.5f };
            var item = new PluginKeybindingListItem(keybind, mode, panel.BackgroundColor, ensureBinding, observeBinding) { Width = StyleDimension.Fill, Height = StyleDimension.Fill };
            item.SetSnapPoint("Wide", order);
            row.Append(item);
            rows.Add(row);
        }
        panel.BackgroundColor = panel.BackgroundColor.MultiplyRGBA(new Color(111, 111, 111));
        group.Append(new UITextPanel<string>(heading, 0.7f) { VAlign = 0f, HAlign = 0.5f });
        group.Recalculate();
        group.Height = new StyleDimension(rows.GetTotalHeight() + 46f, 0f);
        return group;
    }

    private sealed class ControlsState { internal long Version = -1; }
    private sealed class PluginKeybindControlGroup : UISortableElement { internal PluginKeybindControlGroup(int order) : base(order) { } }

    private sealed class PluginKeybindingListItem : UIElement
    {
        private readonly PluginKeybindRegistration keybind; private readonly InputMode mode; private readonly Color color;
        private readonly Action<PluginKeybindRegistration, InputMode> ensure; private readonly Action<PluginKeybindRegistration, InputMode, IReadOnlyList<string>> observe;
        internal PluginKeybindingListItem(PluginKeybindRegistration keybind, InputMode mode, Color color, Action<PluginKeybindRegistration, InputMode> ensure, Action<PluginKeybindRegistration, InputMode, IReadOnlyList<string>> observe)
        { this.keybind = keybind; this.mode = mode; this.color = color; this.ensure = ensure; this.observe = observe; OnLeftClick += Listen; }
        private void Listen(UIMouseEvent _, UIElement __) { PlayerInput.ListenFor(PlayerInput.CurrentProfile.AllowEditing ? keybind.HostId : null, mode); }
        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            CalculatedStyle dimensions = GetDimensions(); ensure(keybind, mode);
            bool listening = PlayerInput.ListeningTrigger == keybind.HostId;
            Color textColor = listening ? Color.Gold : (IsMouseHovering ? Color.White : Color.Silver);
            textColor = Color.Lerp(textColor, Color.White, IsMouseHovering ? 0.5f : 0f);
            Utils.DrawSettingsPanel(spriteBatch, dimensions.Position(), dimensions.Width + 1f, IsMouseHovering ? color : color.MultiplyRGBA(new Color(180, 180, 180)));
            Utils.DrawBorderString(spriteBatch, keybind.Descriptor.DisplayName, dimensions.Position() + new Vector2(8f, 8f), textColor, 0.8f, 0f, 0f, -1);
            IReadOnlyList<string> bindings = PlayerInput.CurrentProfile.InputModes[mode].KeyStatus[keybind.HostId]; observe(keybind, mode, bindings);
            string text = bindings.Count == 0 ? Lang.menu[195].Value : string.Join("/", bindings);
            if (bindings.Count == 0 && !listening) textColor = new Color(80, 80, 80);
            Utils.DrawBorderString(spriteBatch, text, new Vector2(dimensions.X + dimensions.Width - text.Length * 8.8f - 10f, dimensions.Y + 8f), textColor, 0.8f, 0f, 0f, -1);
        }
    }
}
