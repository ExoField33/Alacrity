using System;
using System.Collections.Generic;
using System.Reflection;
using Alacrity.App.PluginManagement;
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

    internal uint IconInteractionInputTick { get; set; } = uint.MaxValue;

    internal bool IconInteractionWasDown { get; set; }

    internal bool IconInteractionPressed { get; set; }

    internal bool IconInteractionConsumed { get; set; }
}
