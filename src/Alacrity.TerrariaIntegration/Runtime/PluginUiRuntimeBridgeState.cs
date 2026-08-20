using System;
using System.Collections.Generic;
using System.Reflection;
using Alacrity.App.PluginManagement;
using Alacrity.PluginSdk;
using AlacrityTerraria.UserInterface;
using Microsoft.Xna.Framework.Graphics;

namespace AlacrityTerraria.Runtime;

/// <summary>
/// Mutable bridge presentation state owned by one managed runtime instance. Keeping this state
/// out of the static ABI facade prevents a patched entry point from becoming an application-wide
/// service locator while retaining the facade's stable method signatures.
/// </summary>
internal sealed class PluginUiRuntimeBridgeState
{
    internal ClientPresentationStateTracker PresentationStates { get; } = new ClientPresentationStateTracker();

    internal PluginManagerPresenter Presenter { get; } = new PluginManagerPresenter();

    internal HashSet<string> ReportedOptionalUiFailures { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal MethodInfo AssetRequest { get; set; }

    internal MethodInfo AssetFrame { get; set; }

    internal PropertyInfo AssetValue { get; set; }

    internal FieldInfo MainAssetsField { get; set; }

    internal Texture2D IngameBlankTexture { get; set; }

    internal GraphicsDevice IngameBlankTextureDevice { get; set; }

    internal bool PluginMenuOpen { get; set; }

    internal PluginUiRuntime.PluginSelectionMenu SelectionMenu { get; set; }

    internal PluginManagerRow[] IngameEntries { get; set; } = Array.Empty<PluginManagerRow>();

    internal int IngameSelectedEntry { get; set; }

    internal int IngameView { get; set; }

    internal float IngameScroll { get; set; }

    internal float IngameDescriptionScroll { get; set; }

    internal string IngameHoveredSettingId { get; set; }

    internal string IngameHoveredPluginActionId { get; set; }

    /// <summary>
    /// Immediate-mode in-game plugin settings capture. A non-empty value owns the primary mouse
    /// button until release so dragging a slider or scrollbar cannot activate Terraria's settings UI.
    /// </summary>
    internal string IngamePointerCaptureId { get; set; }

    /// <summary>Owner-local control whose in-game dropdown currently owns pointer and Escape input.</summary>
    internal string IngameOpenDropdownControlId { get; set; }

    /// <summary>Scroll offset within the currently open in-game dropdown.</summary>
    internal int IngameDropdownScroll { get; set; }

    /// <summary>Fixed top edge for the current chooser. Filtering may shorten only its bottom edge.</summary>
    internal int IngameDropdownTop { get; set; } = -1;

    /// <summary>Current host-owned search text for the in-game dropdown chooser.</summary>
    internal string IngameDropdownSearchText { get; set; } = string.Empty;

    /// <summary>Cursor-aware host search state for the in-game dropdown chooser.</summary>
    internal PluginSearchTextBuffer IngameDropdownSearchBuffer { get; } = new PluginSearchTextBuffer();

    internal PluginSearchKeyRepeatState IngameDropdownBackspaceRepeat;

    internal PluginSearchKeyRepeatState IngameDropdownDeleteRepeat;

    internal PluginSearchKeyRepeatState IngameDropdownLeftRepeat;

    internal PluginSearchKeyRepeatState IngameDropdownRightRepeat;

    /// <summary>Whether typed input is currently directed to the in-game dropdown search box.</summary>
    internal bool IngameDropdownSearchFocused { get; set; }

    /// <summary>Reusable filtered view. It is rebuilt only after a search/input change.</summary>
    internal List<PluginSettingOption> IngameDropdownFilteredOptions { get; } = new List<PluginSettingOption>(128);

    internal uint IconInteractionInputTick { get; set; } = uint.MaxValue;

    internal bool IconInteractionWasDown { get; set; }

    internal bool IconInteractionPressed { get; set; }

    internal bool IconInteractionConsumed { get; set; }
}
