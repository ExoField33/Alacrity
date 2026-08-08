using System;
using System.Linq;
using Alacrity.App;
using Alacrity.App.PluginManagement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Terraria.UI.Gamepad;

namespace AlacrityTerraria;

public static partial class PluginUiRuntime
{
    /// <summary>Workshop-style package description screen owned by the manager UI layer.</summary>
    private sealed class PluginDescriptionMenu : UIState
    {
        private readonly PluginManagerRow plugin;
        private UIGamepadHelper gamepadHelper;
        internal PluginDescriptionMenu(PluginManagerRow plugin) { this.plugin = plugin; }
        public override void OnInitialize()
        {
            var outer = new UIElement { Width = new StyleDimension(0f, 0.8f), MaxWidth = new StyleDimension(500f, 0f), MinWidth = new StyleDimension(300f, 0f), Top = new StyleDimension(230f, 0f), Height = new StyleDimension(-230f, 1f), HAlign = 0.5f };
            Append(outer);
            var panel = new UIPanel { Width = StyleDimension.Fill, Height = new StyleDimension(-110f, 1f), BackgroundColor = new Color(33, 43, 79) * 0.8f }; outer.Append(panel);
            var content = new UIElement { Width = StyleDimension.Fill, Height = StyleDimension.Fill }; panel.Append(content);
            content.Append(new UIText(plugin.Name, 0.935f, true) { HAlign = 0.5f, Top = new StyleDimension(0f, 0f) });
            content.Append(new UIText("Author: " + plugin.Author, 0.8f, false) { HAlign = 0f, VAlign = 0f, Top = new StyleDimension(42f, 0f) });
            content.Append(new UIText("Version: " + plugin.Version, 0.8f, false) { HAlign = 1f, VAlign = 0f, Top = new StyleDimension(42f, 0f) });
            var list = new UIList { Width = new StyleDimension(-25f, 1f), Height = new StyleDimension(-112f, 1f), VAlign = 1f, Top = new StyleDimension(-8f, 0f), ListPadding = 14f, PaddingRight = 12f, ManualSortMethod = items => { } };
            list.Add(CreateSection("Description", plugin.Description, true)); list.Add(CreateSection("Changelog", plugin.Changelog, false)); content.Append(list);
            var scrollbar = new UIScrollbar { Height = new StyleDimension(-112f, 1f), HAlign = 1f, VAlign = 1f, Top = new StyleDimension(-8f, 0f) }; content.Append(scrollbar); list.SetScrollbar(scrollbar);
            var back = new UITextPanel<string>("Back", 0.7f, true) { Width = new StyleDimension(-8f, 0.5f), Height = new StyleDimension(50f, 0f), HAlign = 0.5f, VAlign = 1f, Top = new StyleDimension(-20f, 0f) };
            back.OnMouseOver += (_, element) => FadedMouseOver((UIPanel)element); back.OnMouseOut += (_, element) => FadedMouseOut((UIPanel)element); back.OnLeftClick += (_, __) => ReturnToPluginList(); back.SetSnapPoint("GoBack", 0, null, null); outer.Append(back);
        }
        private static UIElement CreateSection(string heading, string text, bool description)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "No information provided." : text;
            var section = new UIElement { Width = StyleDimension.Fill, Height = new StyleDimension(38f + Math.Max(1, (value.Length + 45) / 46) * 19f + (description ? 14f : 0f), 0f) };
            section.Append(new UIText(heading, 0.68f, true) { Width = StyleDimension.Fill, Height = new StyleDimension(20f, 0f) });
            section.Append(new UIText(value, 0.75f, false) { Width = StyleDimension.Fill, Top = new StyleDimension(description ? 30f : 34f, 0f), IsWrapped = true, WrappedTextBottomPadding = 0f }); return section;
        }
        public override void Draw(SpriteBatch spriteBatch) { base.Draw(spriteBatch); UILinkPointNavigator.Shortcuts.BackButtonCommand = 1; int first = 3700, next = first; foreach (var point in GetSnapPoints().Where(point => point.Name == "GoBack")) gamepadHelper.MakeLinkPointFromSnapPoint(next++, point); gamepadHelper.MoveToVisuallyClosestPoint(first, next); }
        private static void FadedMouseOver(UIPanel panel) { SoundEngine.PlaySound(12, -1, -1, 1, 1f, 0f); panel.BackgroundColor = new Color(73, 94, 171); panel.BorderColor = Colors.FancyUIFatButtonMouseOver; }
        private static void FadedMouseOut(UIPanel panel) { panel.BackgroundColor = new Color(63, 82, 151) * 0.8f; panel.BorderColor = Color.Black; }
    }
}
