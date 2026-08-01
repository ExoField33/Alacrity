using System;

namespace Alacrity.PluginSdk;

/// Supported deterministic player-list ordering modes.
public enum PlayerListSortMode
{
    /// Orders players by their visible name.
    Alphabetical,
    /// Groups teams in Terraria's red-through-pink order.
    Team,
    /// Orders players by current life for local presentation only.
    Health
}

/// Read-only Player List presentation state exposed to dependent plugins.
public interface IPlayerListPresentationState
{
    /// Whether the locally requested player list is currently visible.
    bool IsVisible { get; }

    /// Maximum rows placed in one column.
    int PlayersPerColumn { get; }

    /// Width in UI pixels reserved for each player row.
    int RowWidth { get; }

    /// Multiplier applied consistently to player-list text, player icons, and row geometry.
    float TextScale { get; }

    /// Whether all player-row icons, including ghost and tombstone icons, are rendered.
    bool ShowPlayerHeads { get; }

    /// Whether the local ping footer is rendered.
    bool ShowPing { get; }

    /// Whether suspected automated accounts are omitted from the local list and count.
    bool HideBots { get; }

    /// Current deterministic ordering selected by the local player.
    PlayerListSortMode SortMode { get; }
}

/// Host-mediated local controls owned by the Player List provider.
public interface IPlayerListController
{
    /// Toggles the local list visibility from a registered player-controlled keybind.
    void ToggleVisibility();

    /// Sets the local list visibility for a held player-controlled keybind.
    void SetVisibility(bool isVisible);

    /// Advances through the supported local sorting modes.
    void CycleSortMode();

    /// Toggles whether locally detected automated accounts are excluded from the list.
    void ToggleBotFiltering();
}

/// 
/// Combined Player List provider contract retained for source and package compatibility. New consumers
/// should request only <see cref="IPlayerListPresentationState"/> or <see cref="IPlayerListController"/>
/// according to the access they require.
/// 
public interface IPlayerListService : IPlayerListPresentationState, IPlayerListController
{
}
