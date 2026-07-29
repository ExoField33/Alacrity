using System;
using Alacrity.PluginSdk;

namespace Alacrity.PlayerList;

/// <summary>Owns the local presentation preferences and stable service contract for the player list.</summary>
public sealed class PlayerListPlugin : IAlacrityPlugin, IPlayerListService
{
    private IPluginContext? context;
    private bool visible;
    private int playersPerColumn = 14;
    private int rowWidth = 260;
    private int textScalePercent = 120;
    private bool showPlayerHeads = true;
    private bool showPing = true;
    private bool hideBots;
    private PlayerListSortMode sortMode;
    private PlayerListDisplayMode displayMode;

    public bool IsVisible => visible;
    public int PlayersPerColumn => playersPerColumn;
    public int RowWidth => rowWidth;
    public float TextScale => textScalePercent / 100f;
    public bool ShowPlayerHeads => showPlayerHeads;
    public bool ShowPing => showPing;
    public bool HideBots => hideBots;
    public PlayerListSortMode SortMode => sortMode;

    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        playersPerColumn = Clamp(context.Settings.Get("playersPerColumn", 14), 8, 20);
        rowWidth = Clamp(context.Settings.Get("rowWidth", 260), 180, 420);
        textScalePercent = Clamp(context.Settings.Get("textScalePercent", 120), 80, 160);
        showPlayerHeads = context.Settings.Get("showPlayerHeads", true);
        showPing = context.Settings.Get("showPing", true);
        hideBots = context.Settings.Get("hideBots", false);
        sortMode = ReadSortMode(context.Settings.Get("sortMode", "Alphabetical"));
        displayMode = ReadDisplayMode(context.Settings.Get("displayMode", "Hold"));

        context.Ui.RegisterSettingsPage(new PluginUiContribution("player-list", "Player List"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("players-per-column", "Players Per Column", 8f, 20f, 1f, () => playersPerColumn, value => SetPlayersPerColumn((int)Math.Round(value)), value => ((int)Math.Round(value)).ToString()));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("row-width", "Row Width", 180f, 420f, 5f, () => rowWidth, value => SetRowWidth((int)Math.Round(value / 5f) * 5), value => ((int)Math.Round(value)).ToString()));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Slider("ui-size", "UI Size", 80f, 160f, 5f, () => textScalePercent, value => SetTextScale((int)Math.Round(value / 5f) * 5), value => ((int)Math.Round(value)).ToString() + "%"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("player-heads", "Show Player Icons", () => showPlayerHeads, SetShowPlayerHeads));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("ping", "Show Ping", () => showPing, SetShowPing));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("hide-bots", "Hide Suspected Bots", () => hideBots, SetHideBots));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle("sort", "Sort", new[] { "Alphabetical", "Team", "Health" }, () => sortMode.ToString(), SetSortMode));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Cycle("display-mode", "Display Mode", new[] { "Hold", "Toggle" }, () => displayMode.ToString(), SetDisplayMode));
        context.Keybinds.Register(new PluginKeybindDescriptor("display-player-list", "T", "Display Player List", PluginKeybindActivation.Hold), HandleDisplayKeybind);
        context.Services.Publish<IPlayerListService>(this);
    }

    public void Enable() { }
    public void Disable() => visible = false;
    public void Shutdown() { visible = false; context = null; }

    public void ToggleVisibility() => visible = !visible;
    public void SetVisibility(bool isVisible) => visible = isVisible;
    public void CycleSortMode()
    {
        sortMode = sortMode == PlayerListSortMode.Alphabetical ? PlayerListSortMode.Team : sortMode == PlayerListSortMode.Team ? PlayerListSortMode.Health : PlayerListSortMode.Alphabetical;
        Persist("sortMode", sortMode.ToString());
    }

    public void ToggleBotFiltering()
    {
        hideBots = !hideBots;
        Persist("hideBots", hideBots);
    }

    private void SetPlayersPerColumn(int value) { value = Clamp(value, 8, 20); if (playersPerColumn == value) return; playersPerColumn = value; Persist("playersPerColumn", value); }
    private void SetRowWidth(int value) { value = Clamp(value, 180, 420); if (rowWidth == value) return; rowWidth = value; Persist("rowWidth", value); }
    private void SetTextScale(int value) { value = Clamp(value, 80, 160); if (textScalePercent == value) return; textScalePercent = value; Persist("textScalePercent", value); }
    private void SetShowPlayerHeads(bool value) { if (showPlayerHeads == value) return; showPlayerHeads = value; Persist("showPlayerHeads", value); }
    private void SetShowPing(bool value) { if (showPing == value) return; showPing = value; Persist("showPing", value); }
    private void SetHideBots(bool value) { if (hideBots == value) return; hideBots = value; Persist("hideBots", value); }
    private void SetSortMode(string value) { var next = ReadSortMode(value); if (sortMode == next) return; sortMode = next; Persist("sortMode", sortMode.ToString()); }
    private void SetDisplayMode(string value) { var next = ReadDisplayMode(value); if (displayMode == next) return; displayMode = next; Persist("displayMode", displayMode.ToString()); }
    private void HandleDisplayKeybind(bool isDown)
    {
        if (displayMode == PlayerListDisplayMode.Hold)
            visible = isDown;
        else if (isDown)
            visible = !visible;
    }
    private void Persist<T>(string key, T value) { context?.Settings.Set(key, value); }
    private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
    private static PlayerListSortMode ReadSortMode(string? value) => string.Equals(value, "Team", StringComparison.OrdinalIgnoreCase) ? PlayerListSortMode.Team : string.Equals(value, "Health", StringComparison.OrdinalIgnoreCase) ? PlayerListSortMode.Health : PlayerListSortMode.Alphabetical;
    private static PlayerListDisplayMode ReadDisplayMode(string? value) => string.Equals(value, "Toggle", StringComparison.OrdinalIgnoreCase) ? PlayerListDisplayMode.Toggle : PlayerListDisplayMode.Hold;

    private enum PlayerListDisplayMode
    {
        Hold,
        Toggle
    }
}
