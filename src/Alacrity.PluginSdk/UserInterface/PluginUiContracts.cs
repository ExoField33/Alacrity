using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// Registers UI contributions; the host controls actual layout and rendering.
public interface IPluginUiService
{
    /// Registers a settings-page contribution.
    IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution);

    /// Registers a host-rendered interactive setting owned by the current plugin.
    IPluginRegistration RegisterSettingsControl(PluginUiContribution contribution);

    /// Registers a typed host-rendered setting control owned by the current plugin.
    IPluginRegistration RegisterSettingsControl(PluginSettingControl control);

    /// Registers a host-evaluated icon interaction for use by a retained or immediate-mode Terraria surface.
    IPluginRegistration RegisterIconInteraction(PluginIconInteractionDescriptor descriptor, Action activate);

    /// Registers legacy overlay metadata. Use <see cref="IPluginContext.Overlays"/> for drawing callbacks.
    [Obsolete("Use IPluginContext.Overlays for draw callbacks. This retained UI metadata API is compatibility-only.")]
    IPluginRegistration RegisterOverlay(PluginUiContribution contribution);
}

/// Host-neutral rectangle used for pointer hit testing.
public readonly struct PluginUiRect
{
    /// Creates a host-neutral rectangle in the coordinate space of the rendering surface.
    public PluginUiRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    /// Gets the left coordinate.
    public float X { get; }
    /// Gets the top coordinate.
    public float Y { get; }
    /// Gets the rectangle width.
    public float Width { get; }
    /// Gets the rectangle height.
    public float Height { get; }
    /// Determines whether a point is inside a non-empty rectangle.
    public bool Contains(float x, float y) => hasArea && x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    private bool hasArea => Width > 0f && Height > 0f;
}

/// Supported visual response when the player hovers a registered icon.
public enum PluginIconHoverEffect
{
    /// Leaves the icon visually unchanged.
    None,
    /// Applies the configured hover color.
    Highlight,
    /// Applies the configured hover scale.
    Expand,
    /// Applies both the configured hover color and scale.
    HighlightAndExpand
}

/// Host-selected placement for a registered icon tooltip.
public enum PluginTooltipPlacement
{
    /// Places the tooltip beside the pointer.
    Mouse,
    /// Places the tooltip to the left of the pointer.
    Left,
    /// Places the tooltip to the right of the pointer.
    Right,
    /// Places the tooltip above the pointer.
    Above,
    /// Places the tooltip below the pointer.
    Below
}

/// Immutable host-rendered tooltip options for an interactive icon.
public sealed class PluginTooltipOptions
{
    /// Creates immutable presentation options for an icon tooltip.
    public PluginTooltipOptions(string text, PluginTooltipPlacement placement = PluginTooltipPlacement.Mouse, PluginColor? color = null, float scale = 1f)
    {
        Text = string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("Tooltip text is required.", nameof(text)) : text;
        if (!Enum.IsDefined(typeof(PluginTooltipPlacement), placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        Placement = placement; Color = color; Scale = scale;
    }
    /// Gets the displayed text.
    public string Text { get; }
    /// Gets the requested pointer-relative placement.
    public PluginTooltipPlacement Placement { get; }
    /// Gets the optional text color.
    public PluginColor? Color { get; }
    /// Gets the host-relative text scale.
    public float Scale { get; }
}

/// Immutable declaration for one owner-local icon interaction.
public sealed class PluginIconInteractionDescriptor
{
    /// Creates an immutable owner-local icon interaction declaration.
    public PluginIconInteractionDescriptor(string id, PluginIconHoverEffect hoverEffect = PluginIconHoverEffect.Highlight, float hoverScale = 1.15f, PluginColor? normalColor = null, PluginColor? hoverColor = null, PluginTooltipOptions? tooltip = null)
        : this(id, hoverEffect, hoverScale, normalColor, hoverColor, tooltip, null)
    {
    }

    /// Creates an immutable owner-local icon interaction declaration.
    public PluginIconInteractionDescriptor(string id, PluginIconHoverEffect hoverEffect, float hoverScale, PluginColor? normalColor, PluginColor? hoverColor, PluginTooltipOptions? tooltip, Func<PluginTooltipOptions?>? tooltipProvider)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("An icon interaction ID is required.", nameof(id)) : id;
        if (!Enum.IsDefined(typeof(PluginIconHoverEffect), hoverEffect)) throw new ArgumentOutOfRangeException(nameof(hoverEffect));
        if (float.IsNaN(hoverScale) || float.IsInfinity(hoverScale) || hoverScale < 1f || hoverScale > 2f) throw new ArgumentOutOfRangeException(nameof(hoverScale));
        HoverEffect = hoverEffect; HoverScale = hoverScale; NormalColor = normalColor; HoverColor = hoverColor; Tooltip = tooltip; TooltipProvider = tooltipProvider;
    }
    /// Gets the owner-local stable interaction identifier.
    public string Id { get; }
    /// Gets the requested hover visual effect.
    public PluginIconHoverEffect HoverEffect { get; }
    /// Gets the scale applied for expanding hover effects.
    public float HoverScale { get; }
    /// Gets the optional normal icon color.
    public PluginColor? NormalColor { get; }
    /// Gets the optional highlighted icon color.
    public PluginColor? HoverColor { get; }
    /// Gets the optional tooltip declaration.
    public PluginTooltipOptions? Tooltip { get; }
    /// Gets an optional hover-time tooltip resolver for state-dependent labels.
    public Func<PluginTooltipOptions?>? TooltipProvider { get; }
}

/// Resolved visual and activation state returned by the host for the current pointer position.
public readonly struct PluginIconInteractionState
{
    /// Creates the state resolved by the host for the current pointer position.
    public PluginIconInteractionState(bool isRegistered, bool isHovered, float scale, PluginColor? color, PluginTooltipOptions? tooltip) { IsRegistered = isRegistered; IsHovered = isHovered; Scale = scale; Color = color; Tooltip = tooltip; }
    /// Gets whether the requested owner-local interaction remains active.
    public bool IsRegistered { get; }
    /// Gets whether the current pointer is within the supplied bounds.
    public bool IsHovered { get; }
    /// Gets the host-resolved icon scale.
    public float Scale { get; }
    /// Gets the host-resolved icon color.
    public PluginColor? Color { get; }
    /// Gets the tooltip to render while hovered.
    public PluginTooltipOptions? Tooltip { get; }
}

/// Host-rendered UI contribution metadata.
public sealed class PluginUiContribution
{
    /// Creates a contribution declaration.
    public PluginUiContribution(string id, string displayName)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A UI contribution ID is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
    }

    /// Creates an interactive setting with plugin-owned value and activation delegates.
    public PluginUiContribution(string id, string displayName, Func<string> valueText, Action activate)
        : this(id, displayName)
    {
        ValueText = valueText ?? throw new ArgumentNullException(nameof(valueText));
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    /// Stable contribution identifier within the current plugin.
    public string Id { get; }

    /// User-facing display name.
    public string DisplayName { get; }

    /// Gets the current setting value while the contribution is visible.
    public Func<string>? ValueText { get; }

    /// Changes the setting when the player activates the control.
    public Action? Activate { get; }

    /// Whether this contribution is a host-rendered interactive setting.
    public bool IsInteractive => ValueText != null && Activate != null;
}

/// Host-rendered setting control kinds supported by the stable plugin UI contract.
public enum PluginSettingControlKind
{
    /// A two-state enabled or disabled control.
    Toggle,
    /// A control which cycles through declared values.
    Cycle,
    /// A bounded numeric slider.
    Slider,
    /// A three-channel color picker with hexadecimal import and export.
    Color
}

/// Terraria-independent RGB color used by plugin settings contracts.
public readonly struct PluginColor : IEquatable<PluginColor>
{
    /// Creates an opaque RGB color.
    public PluginColor(byte red, byte green, byte blue) { Red = red; Green = green; Blue = blue; }
    /// Red channel.
    public byte Red { get; }
    /// Green channel.
    public byte Green { get; }
    /// Blue channel.
    public byte Blue { get; }
    /// Formats the color as a six-digit hexadecimal value.
    public string ToHex() => "#" + Red.ToString("X2") + Green.ToString("X2") + Blue.ToString("X2");
    /// Parses a #RRGGBB or RRGGBB color.
    public static bool TryParseHex(string? value, out PluginColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string hex = value!.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
        if (hex.Length != 6 || !byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte red) || !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte green) || !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte blue)) return false;
        color = new PluginColor(red, green, blue);
        return true;
    }
    /// <inheritdoc />
    public bool Equals(PluginColor other) => Red == other.Red && Green == other.Green && Blue == other.Blue;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PluginColor other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => (Red << 16) | (Green << 8) | Blue;
}

/// Typed plugin setting metadata and host callbacks. Factories validate their stable declarations eagerly.
public sealed class PluginSettingControl
{
    private PluginSettingControl(string id, string displayName, PluginSettingControlKind kind)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A setting control ID is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A setting display name is required.", nameof(displayName)) : displayName;
        Kind = kind;
    }

    /// Stable control ID within the owning plugin.
    public string Id { get; }
    /// User-facing setting label.
    public string DisplayName { get; }
    /// How the host renders and edits this control.
    public PluginSettingControlKind Kind { get; }
    /// Owning settings page ID when the plugin registered this control through the retained page model.
    public string? PageId { get; private set; }
    /// Reads the current toggle value.
    public Func<bool>? GetToggle { get; private set; }
    /// Writes a toggle value.
    public Action<bool>? SetToggle { get; private set; }
    /// Declared cycle values in display order.
    public IReadOnlyList<string>? CycleValues { get; private set; }
    /// Reads the current cycle value.
    public Func<string>? GetCycle { get; private set; }
    /// Writes a cycle value.
    public Action<string>? SetCycle { get; private set; }
    /// Inclusive slider minimum.
    public float Minimum { get; private set; }
    /// Inclusive slider maximum.
    public float Maximum { get; private set; }
    /// Slider increment, or zero for continuous movement.
    public float Step { get; private set; }
    /// Reads the slider value.
    public Func<float>? GetSlider { get; private set; }
    /// Writes the slider value.
    public Action<float>? SetSlider { get; private set; }
    /// Optional slider display formatter.
    public Func<float, string>? FormatSlider { get; private set; }
    /// Reads the RGB color.
    public Func<PluginColor>? GetColor { get; private set; }
    /// Writes the RGB color.
    public Action<PluginColor>? SetColor { get; private set; }

    /// Creates a Terraria-style enabled or disabled setting.
    public static PluginSettingControl Toggle(string id, string displayName, Func<bool> getValue, Action<bool> setValue)
    {
        var control = new PluginSettingControl(id, displayName, PluginSettingControlKind.Toggle) { GetToggle = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetToggle = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
        return control;
    }

    /// Creates a toggle directly bound to a host-owned typed setting.
    public static PluginSettingControl Toggle(string id, string displayName, IPluginSetting<bool> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Toggle(id, displayName, () => setting.Value, value => setting.Value = value);
    }

    /// Creates a Terraria-style cycling setting.
    public static PluginSettingControl Cycle(string id, string displayName, IReadOnlyList<string> values, Func<string> getValue, Action<string> setValue)
    {
        if (values == null || values.Count < 2 || values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A cycle needs at least two non-empty values.", nameof(values));
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Cycle) { CycleValues = values.ToArray(), GetCycle = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetCycle = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
    }

    /// Creates a cycle directly bound to a host-owned string setting.
    public static PluginSettingControl Cycle(string id, string displayName, IReadOnlyList<string> values, IPluginSetting<string> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Cycle(id, displayName, values, () => setting.Value, value => setting.Value = value);
    }

    /// Creates a bounded Terraria-style numeric slider.
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, Func<float> getValue, Action<float> setValue, Func<float, string>? formatter = null)
    {
        if (float.IsNaN(minimum) || float.IsNaN(maximum) || minimum >= maximum || step < 0f) throw new ArgumentOutOfRangeException(nameof(minimum), "Slider bounds or step are invalid.");
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Slider) { Minimum = minimum, Maximum = maximum, Step = step, GetSlider = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetSlider = setValue ?? throw new ArgumentNullException(nameof(setValue)), FormatSlider = formatter };
    }

    /// Creates a slider directly bound to a host-owned float setting.
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, IPluginSetting<float> setting, Func<float, string>? formatter = null)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Slider(id, displayName, minimum, maximum, step, () => setting.Value, value => setting.Value = value, formatter);
    }

    /// Creates a discrete slider directly bound to a host-owned integer setting.
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, IPluginSetting<int> setting, Func<float, string>? formatter = null)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Slider(id, displayName, minimum, maximum, step, () => setting.Value, value => setting.Value = (int)Math.Round(value), formatter);
    }

    /// Creates a three-channel Terraria-style color picker with host-owned hexadecimal copy and paste.
    public static PluginSettingControl Color(string id, string displayName, Func<PluginColor> getValue, Action<PluginColor> setValue)
    {
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Color) { GetColor = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetColor = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
    }

    /// Creates a color control directly bound to a host-owned color setting.
    public static PluginSettingControl Color(string id, string displayName, IPluginSetting<PluginColor> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Color(id, displayName, () => setting.Value, value => setting.Value = value);
    }

    /// Creates a color control bound to a legacy hexadecimal string setting without changing its persisted format.
    public static PluginSettingControl Color(string id, string displayName, IPluginSetting<string> setting, PluginColor defaultValue)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Color(id, displayName, () => PluginColor.TryParseHex(setting.Value, out PluginColor value) ? value : defaultValue, value => setting.Value = value.ToHex());
    }

    /// Associates this retained control with one owner-local settings page.
    public PluginSettingControl InPage(string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId)) throw new ArgumentException("A settings page ID is required.", nameof(pageId));
        if (PageId != null && !string.Equals(PageId, pageId, StringComparison.Ordinal)) throw new InvalidOperationException("A setting control cannot be assigned to more than one page.");
        PageId = pageId;
        return this;
    }

}

