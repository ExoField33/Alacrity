using System;

namespace Alacrity.PluginSdk;

/// <summary>Supported deterministic player-list ordering modes.</summary>
public enum PlayerListSortMode
{
    /// <summary>Orders players by their visible name.</summary>
    Alphabetical,
    /// <summary>Groups teams in Terraria's red-through-pink order.</summary>
    Team,
    /// <summary>Orders players by current life for local presentation only.</summary>
    Health
}

/// <summary>Read-only Player List presentation state exposed to dependent plugins.</summary>
public interface IPlayerListPresentationState
{
    /// <summary>Whether the locally requested player list is currently visible.</summary>
    bool IsVisible { get; }

    /// <summary>Maximum rows placed in one column.</summary>
    int PlayersPerColumn { get; }

    /// <summary>Width in UI pixels reserved for each player row.</summary>
    int RowWidth { get; }

    /// <summary>Multiplier applied consistently to player-list text, player icons, and row geometry.</summary>
    float TextScale { get; }

    /// <summary>Whether all player-row icons, including ghost and tombstone icons, are rendered.</summary>
    bool ShowPlayerHeads { get; }

    /// <summary>Whether the local ping footer is rendered.</summary>
    bool ShowPing { get; }

    /// <summary>Whether suspected automated accounts are omitted from the local list and count.</summary>
    bool HideBots { get; }

    /// <summary>Current deterministic ordering selected by the local player.</summary>
    PlayerListSortMode SortMode { get; }
}

/// <summary>Host-mediated local controls owned by the Player List provider.</summary>
public interface IPlayerListController
{
    /// <summary>Toggles the local list visibility from a registered player-controlled keybind.</summary>
    void ToggleVisibility();

    /// <summary>Sets the local list visibility for a held player-controlled keybind.</summary>
    void SetVisibility(bool isVisible);

    /// <summary>Advances through the supported local sorting modes.</summary>
    void CycleSortMode();

    /// <summary>Toggles whether locally detected automated accounts are excluded from the list.</summary>
    void ToggleBotFiltering();
}

/// <summary>
/// Combined Player List provider contract retained for source and package compatibility. New consumers
/// should request only <see cref="IPlayerListPresentationState"/> or <see cref="IPlayerListController"/>
/// according to the access they require.
/// </summary>
public interface IPlayerListService : IPlayerListPresentationState, IPlayerListController
{
}
