using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>One host-owned registration that is released with its plugin resource scope.</summary>
public interface IPluginRegistration : IDisposable
{
    /// <summary>Stable diagnostic name for the registration.</summary>
    string Name { get; }

    /// <summary>Whether the host has released the registration.</summary>
    bool IsReleased { get; }
}

/// <summary>Asynchronous lifecycle for plugins loaded from a host-verified package manifest.</summary>
public interface IAsyncAlacrityPlugin
{
    /// <summary>Initializes plugin state from the host-supplied verified context.</summary>
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);

    /// <summary>Activates registrations and runtime work.</summary>
    Task EnableAsync(CancellationToken cancellationToken);

    /// <summary>Stops runtime work before scope cleanup.</summary>
    Task DisableAsync(CancellationToken cancellationToken);

    /// <summary>Releases plugin-owned managed state.</summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>Plugin-scoped typed settings boundary. Persistence and recovery are host-owned.</summary>
public interface IPluginSettings
{
    /// <summary>Registers a typed setting whose persistence is owned by the host.</summary>
    IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition);

    /// <summary>Gets a stored value or the supplied default.</summary>
    T Get<T>(string key, T defaultValue);

    /// <summary>Stores a validated setting value.</summary>
    void Set<T>(string key, T value);

    /// <summary>Removes a stored key.</summary>
    bool Remove(string key);

    /// <summary>Restores the plugin's registered default values.</summary>
    void ResetToDefaults();

    /// <summary>Raised after a setting changes.</summary>
    event EventHandler<PluginSettingChangedEventArgs> Changed;
}

/// <summary>Immutable declaration for one plugin-owned typed setting.</summary>
public sealed class PluginSettingDefinition<T>
{
    /// <summary>Creates a typed setting declaration with an optional normalizer.</summary>
    public PluginSettingDefinition(string key, T defaultValue, Func<T, T>? normalize = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        DefaultValue = defaultValue;
        Normalize = normalize;
    }

    /// <summary>Stable persisted key within the plugin namespace.</summary>
    public string Key { get; }
    /// <summary>Value returned when no valid persisted value exists.</summary>
    public T DefaultValue { get; }
    /// <summary>Optional host-applied normalization before values are exposed or persisted.</summary>
    public Func<T, T>? Normalize { get; }
}

/// <summary>Host-owned typed setting handle. Subscriptions are released with the owning plugin scope.</summary>
public interface IPluginSetting<T>
{
    /// <summary>Stable persisted key within the owning plugin namespace.</summary>
    string Key { get; }
    /// <summary>Declared default value.</summary>
    T DefaultValue { get; }
    /// <summary>Current normalized persisted value.</summary>
    T Value { get; set; }
    /// <summary>Restores the declared default value.</summary>
    void Reset();
    /// <summary>Subscribes to value changes with host-managed lifetime ownership.</summary>
    IPluginRegistration Subscribe(Action<T> handler);
}

/// <summary>Describes one plugin setting change.</summary>
public sealed class PluginSettingChangedEventArgs : EventArgs
{
    /// <summary>Creates a setting change notification.</summary>
    public PluginSettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>Changed key within the current plugin's settings namespace.</summary>
    public string Key { get; }

    /// <summary>Previous value, when one existed.</summary>
    public object? OldValue { get; }

    /// <summary>New value, when one exists.</summary>
    public object? NewValue { get; }
}

/// <summary>Path-confined storage for one plugin's data directory.</summary>
public interface IPluginStorage
{
    /// <summary>Opens a plugin-owned relative file for reading.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Creates or replaces a plugin-owned relative file.</summary>
    Stream Create(string relativePath);

    /// <summary>Checks a plugin-owned relative path.</summary>
    bool Exists(string relativePath);

    /// <summary>Deletes a plugin-owned relative file.</summary>
    void Delete(string relativePath);

    /// <summary>Lists paths beneath a plugin-owned relative directory.</summary>
    IReadOnlyList<string> Enumerate(string relativeDirectory);
}

/// <summary>Typed snapshot event subscriptions. Handlers run on the host-documented affinity for each event.</summary>
public interface IPluginEventService
{
    /// <summary>Subscribes a handler that is automatically removed when its resource scope is released.</summary>
    IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null);
}

/// <summary>Subscription delivery options.</summary>
public sealed class PluginEventOptions
{
    /// <summary>Whether host dispatch should stop this subscription after its first delivery.</summary>
    public bool Once { get; set; }
}

/// <summary>Registers validated plugin commands.</summary>
public interface IPluginCommandService
{
    /// <summary>Registers a command owned by the current plugin.</summary>
    IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler);
}

/// <summary>Explicit result of host command dispatch. A failed registered command is still consumed locally.</summary>
public enum PluginCommandDispatchResult
{
    /// <summary>No plugin owns the requested command.</summary>
    NotFound,
    /// <summary>A plugin command handled the invocation successfully.</summary>
    Handled,
    /// <summary>A plugin command was found and consumed but its callback failed.</summary>
    HandledWithFailure
}

/// <summary>Immutable command declaration.</summary>
public sealed class PluginCommandDescriptor
{
    /// <summary>Creates a command declaration.</summary>
    public PluginCommandDescriptor(string id, string helpText)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A command ID is required.", nameof(id)) : id;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? throw new ArgumentException("Help text is required.", nameof(helpText)) : helpText;
    }

    /// <summary>Stable command identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>User-facing help text.</summary>
    public string HelpText { get; }
}

/// <summary>Validated command invocation arguments.</summary>
public sealed class PluginCommandInvocation
{
    /// <summary>Creates an invocation snapshot.</summary>
    public PluginCommandInvocation(IReadOnlyList<string> arguments, Action<string>? reply = null)
    {
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        this.reply = reply;
    }

    private readonly Action<string>? reply;

    /// <summary>Immutable argument list.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Shows host-owned local feedback for this user-issued command when that UI is available.</summary>
    public void Reply(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A reply message is required.", nameof(message));
        reply?.Invoke(message);
    }
}

/// <summary>Registers user-rebindable plugin keybinds.</summary>
public interface IPluginKeybindService
{
    /// <summary>Registers a keybind owned by the current plugin.</summary>
    IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler);

    /// <summary>Registers a held keybind that receives true on press and false on release.</summary>
    IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action<bool> stateHandler);
}

/// <summary>Determines whether a plugin keybind is invoked once or tracks its held state.</summary>
public enum PluginKeybindActivation
{
    /// <summary>Invokes the handler once for each fresh press.</summary>
    Press,
    /// <summary>Invokes the handler when the binding is pressed and released.</summary>
    Hold
}

/// <summary>Immutable keybind declaration.</summary>
public sealed class PluginKeybindDescriptor
{
    /// <summary>Creates a press-activated keybind declaration.</summary>
    public PluginKeybindDescriptor(string id, string defaultBinding, string displayName)
        : this(id, defaultBinding, displayName, PluginKeybindActivation.Press)
    {
    }

    /// <summary>Creates a keybind declaration with an explicit activation mode.</summary>
    public PluginKeybindDescriptor(string id, string defaultBinding, string displayName, PluginKeybindActivation activation)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A keybind ID is required.", nameof(id)) : id;
        DefaultBinding = string.IsNullOrWhiteSpace(defaultBinding) ? throw new ArgumentException("A default binding is required.", nameof(defaultBinding)) : defaultBinding;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
        if (!Enum.IsDefined(typeof(PluginKeybindActivation), activation)) throw new ArgumentOutOfRangeException(nameof(activation));
        Activation = activation;
    }

    /// <summary>Stable keybind identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>Host-parseable default binding.</summary>
    public string DefaultBinding { get; }

    /// <summary>User-facing label.</summary>
    public string DisplayName { get; }

    /// <summary>Whether this binding activates once or follows its held state.</summary>
    public PluginKeybindActivation Activation { get; }
}

/// <summary>Immutable host-provided keybind row used by Terraria's controls-menu adapter.</summary>
public sealed class PluginKeybindRegistration
{
    /// <summary>Creates a row owned by a verified plugin package.</summary>
    public PluginKeybindRegistration(PluginId owner, string heading, PluginKeybindDescriptor descriptor)
        : this(owner, heading, descriptor, 0)
    {
    }

    /// <summary>Creates a row with host-owned monotonic registration ordering metadata.</summary>
    public PluginKeybindRegistration(PluginId owner, string heading, PluginKeybindDescriptor descriptor, long registrationSequence)
    {
        if (!owner.IsValid) throw new ArgumentException("A valid plugin owner is required.", nameof(owner));
        if (registrationSequence < 0) throw new ArgumentOutOfRangeException(nameof(registrationSequence));
        Owner = owner;
        Heading = string.IsNullOrWhiteSpace(heading) ? throw new ArgumentException("A heading is required.", nameof(heading)) : heading;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        RegistrationSequence = registrationSequence;
    }

    /// <summary>Verified plugin package that owns this binding.</summary>
    public PluginId Owner { get; }
    /// <summary>Plugin heading appended after Terraria's built-in control groups.</summary>
    public string Heading { get; }
    /// <summary>Binding declaration and default.</summary>
    public PluginKeybindDescriptor Descriptor { get; }

    /// <summary>Host-owned monotonic ordering number; never reused after a registration is removed.</summary>
    public long RegistrationSequence { get; }

    /// <summary>Stable host key used by Terraria input-profile adapters. It is unique across plugin packages.</summary>
    public string HostId => Owner.Value + "." + Descriptor.Id;
}

/// <summary>Atomic immutable view of the host-owned keybind registry.</summary>
public sealed class PluginKeybindRegistrySnapshot
{
    /// <summary>Creates one snapshot returned under the host registry lock.</summary>
    public PluginKeybindRegistrySnapshot(long version, IReadOnlyList<PluginKeybindRegistration> registrations)
    {
        Version = version;
        Registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
    }

    /// <summary>Changes whenever any registration is added or removed.</summary>
    public long Version { get; }

    /// <summary>Registrations in deterministic owner and registration order.</summary>
    public IReadOnlyList<PluginKeybindRegistration> Registrations { get; }
}

/// <summary>Registers UI contributions; the host controls actual layout and rendering.</summary>
public interface IPluginUiService
{
    /// <summary>Registers a settings-page contribution.</summary>
    IPluginRegistration RegisterSettingsPage(PluginUiContribution contribution);

    /// <summary>Registers a host-rendered interactive setting owned by the current plugin.</summary>
    IPluginRegistration RegisterSettingsControl(PluginUiContribution contribution);

    /// <summary>Registers a typed host-rendered setting control owned by the current plugin.</summary>
    IPluginRegistration RegisterSettingsControl(PluginSettingControl control);

    /// <summary>Registers a host-evaluated icon interaction for use by a retained or immediate-mode Terraria surface.</summary>
    IPluginRegistration RegisterIconInteraction(PluginIconInteractionDescriptor descriptor, Action activate);

    /// <summary>Registers an overlay contribution.</summary>
    IPluginRegistration RegisterOverlay(PluginUiContribution contribution);
}

/// <summary>Host-neutral rectangle used for pointer hit testing.</summary>
public readonly struct PluginUiRect
{
    /// <summary>Creates a host-neutral rectangle in the coordinate space of the rendering surface.</summary>
    public PluginUiRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    /// <summary>Gets the left coordinate.</summary>
    public float X { get; }
    /// <summary>Gets the top coordinate.</summary>
    public float Y { get; }
    /// <summary>Gets the rectangle width.</summary>
    public float Width { get; }
    /// <summary>Gets the rectangle height.</summary>
    public float Height { get; }
    /// <summary>Determines whether a point is inside a non-empty rectangle.</summary>
    public bool Contains(float x, float y) => hasArea && x >= X && x <= X + Width && y >= Y && y <= Y + Height;
    private bool hasArea => Width > 0f && Height > 0f;
}

/// <summary>Supported visual response when the player hovers a registered icon.</summary>
public enum PluginIconHoverEffect
{
    /// <summary>Leaves the icon visually unchanged.</summary>
    None,
    /// <summary>Applies the configured hover color.</summary>
    Highlight,
    /// <summary>Applies the configured hover scale.</summary>
    Expand,
    /// <summary>Applies both the configured hover color and scale.</summary>
    HighlightAndExpand
}

/// <summary>Host-selected placement for a registered icon tooltip.</summary>
public enum PluginTooltipPlacement
{
    /// <summary>Places the tooltip beside the pointer.</summary>
    Mouse,
    /// <summary>Places the tooltip to the left of the pointer.</summary>
    Left,
    /// <summary>Places the tooltip to the right of the pointer.</summary>
    Right,
    /// <summary>Places the tooltip above the pointer.</summary>
    Above,
    /// <summary>Places the tooltip below the pointer.</summary>
    Below
}

/// <summary>Immutable host-rendered tooltip options for an interactive icon.</summary>
public sealed class PluginTooltipOptions
{
    /// <summary>Creates immutable presentation options for an icon tooltip.</summary>
    public PluginTooltipOptions(string text, PluginTooltipPlacement placement = PluginTooltipPlacement.Mouse, PluginColor? color = null, float scale = 1f)
    {
        Text = string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("Tooltip text is required.", nameof(text)) : text;
        if (!Enum.IsDefined(typeof(PluginTooltipPlacement), placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        Placement = placement; Color = color; Scale = scale;
    }
    /// <summary>Gets the displayed text.</summary>
    public string Text { get; }
    /// <summary>Gets the requested pointer-relative placement.</summary>
    public PluginTooltipPlacement Placement { get; }
    /// <summary>Gets the optional text color.</summary>
    public PluginColor? Color { get; }
    /// <summary>Gets the host-relative text scale.</summary>
    public float Scale { get; }
}

/// <summary>Immutable declaration for one owner-local icon interaction.</summary>
public sealed class PluginIconInteractionDescriptor
{
    /// <summary>Creates an immutable owner-local icon interaction declaration.</summary>
    public PluginIconInteractionDescriptor(string id, PluginIconHoverEffect hoverEffect = PluginIconHoverEffect.Highlight, float hoverScale = 1.15f, PluginColor? normalColor = null, PluginColor? hoverColor = null, PluginTooltipOptions? tooltip = null)
        : this(id, hoverEffect, hoverScale, normalColor, hoverColor, tooltip, null)
    {
    }

    /// <summary>Creates an immutable owner-local icon interaction declaration.</summary>
    public PluginIconInteractionDescriptor(string id, PluginIconHoverEffect hoverEffect, float hoverScale, PluginColor? normalColor, PluginColor? hoverColor, PluginTooltipOptions? tooltip, Func<PluginTooltipOptions?>? tooltipProvider)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("An icon interaction ID is required.", nameof(id)) : id;
        if (!Enum.IsDefined(typeof(PluginIconHoverEffect), hoverEffect)) throw new ArgumentOutOfRangeException(nameof(hoverEffect));
        if (float.IsNaN(hoverScale) || float.IsInfinity(hoverScale) || hoverScale < 1f || hoverScale > 2f) throw new ArgumentOutOfRangeException(nameof(hoverScale));
        HoverEffect = hoverEffect; HoverScale = hoverScale; NormalColor = normalColor; HoverColor = hoverColor; Tooltip = tooltip; TooltipProvider = tooltipProvider;
    }
    /// <summary>Gets the owner-local stable interaction identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the requested hover visual effect.</summary>
    public PluginIconHoverEffect HoverEffect { get; }
    /// <summary>Gets the scale applied for expanding hover effects.</summary>
    public float HoverScale { get; }
    /// <summary>Gets the optional normal icon color.</summary>
    public PluginColor? NormalColor { get; }
    /// <summary>Gets the optional highlighted icon color.</summary>
    public PluginColor? HoverColor { get; }
    /// <summary>Gets the optional tooltip declaration.</summary>
    public PluginTooltipOptions? Tooltip { get; }
    /// <summary>Gets an optional hover-time tooltip resolver for state-dependent labels.</summary>
    public Func<PluginTooltipOptions?>? TooltipProvider { get; }
}

/// <summary>Resolved visual and activation state returned by the host for the current pointer position.</summary>
public readonly struct PluginIconInteractionState
{
    /// <summary>Creates the state resolved by the host for the current pointer position.</summary>
    public PluginIconInteractionState(bool isRegistered, bool isHovered, float scale, PluginColor? color, PluginTooltipOptions? tooltip) { IsRegistered = isRegistered; IsHovered = isHovered; Scale = scale; Color = color; Tooltip = tooltip; }
    /// <summary>Gets whether the requested owner-local interaction remains active.</summary>
    public bool IsRegistered { get; }
    /// <summary>Gets whether the current pointer is within the supplied bounds.</summary>
    public bool IsHovered { get; }
    /// <summary>Gets the host-resolved icon scale.</summary>
    public float Scale { get; }
    /// <summary>Gets the host-resolved icon color.</summary>
    public PluginColor? Color { get; }
    /// <summary>Gets the tooltip to render while hovered.</summary>
    public PluginTooltipOptions? Tooltip { get; }
}

/// <summary>Host-rendered UI contribution metadata.</summary>
public sealed class PluginUiContribution
{
    /// <summary>Creates a contribution declaration.</summary>
    public PluginUiContribution(string id, string displayName)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A UI contribution ID is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
    }

    /// <summary>Creates an interactive setting with plugin-owned value and activation delegates.</summary>
    public PluginUiContribution(string id, string displayName, Func<string> valueText, Action activate)
        : this(id, displayName)
    {
        ValueText = valueText ?? throw new ArgumentNullException(nameof(valueText));
        Activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    /// <summary>Stable contribution identifier within the current plugin.</summary>
    public string Id { get; }

    /// <summary>User-facing display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the current setting value while the contribution is visible.</summary>
    public Func<string>? ValueText { get; }

    /// <summary>Changes the setting when the player activates the control.</summary>
    public Action? Activate { get; }

    /// <summary>Whether this contribution is a host-rendered interactive setting.</summary>
    public bool IsInteractive => ValueText != null && Activate != null;
}

/// <summary>Host-rendered setting control kinds supported by the stable plugin UI contract.</summary>
public enum PluginSettingControlKind
{
    /// <summary>A two-state enabled or disabled control.</summary>
    Toggle,
    /// <summary>A control which cycles through declared values.</summary>
    Cycle,
    /// <summary>A bounded numeric slider.</summary>
    Slider,
    /// <summary>A three-channel color picker with hexadecimal import and export.</summary>
    Color
}

/// <summary>Terraria-independent RGB color used by plugin settings contracts.</summary>
public readonly struct PluginColor : IEquatable<PluginColor>
{
    /// <summary>Creates an opaque RGB color.</summary>
    public PluginColor(byte red, byte green, byte blue) { Red = red; Green = green; Blue = blue; }
    /// <summary>Red channel.</summary>
    public byte Red { get; }
    /// <summary>Green channel.</summary>
    public byte Green { get; }
    /// <summary>Blue channel.</summary>
    public byte Blue { get; }
    /// <summary>Formats the color as a six-digit hexadecimal value.</summary>
    public string ToHex() => "#" + Red.ToString("X2") + Green.ToString("X2") + Blue.ToString("X2");
    /// <summary>Parses a #RRGGBB or RRGGBB color.</summary>
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

/// <summary>Typed plugin setting metadata and host callbacks. Factories validate their stable declarations eagerly.</summary>
public sealed class PluginSettingControl
{
    private PluginSettingControl(string id, string displayName, PluginSettingControlKind kind)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A setting control ID is required.", nameof(id)) : id;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A setting display name is required.", nameof(displayName)) : displayName;
        Kind = kind;
    }

    /// <summary>Stable control ID within the owning plugin.</summary>
    public string Id { get; }
    /// <summary>User-facing setting label.</summary>
    public string DisplayName { get; }
    /// <summary>How the host renders and edits this control.</summary>
    public PluginSettingControlKind Kind { get; }
    /// <summary>Owning settings page ID when the plugin registered this control through the retained page model.</summary>
    public string? PageId { get; private set; }
    /// <summary>Reads the current toggle value.</summary>
    public Func<bool>? GetToggle { get; private set; }
    /// <summary>Writes a toggle value.</summary>
    public Action<bool>? SetToggle { get; private set; }
    /// <summary>Declared cycle values in display order.</summary>
    public IReadOnlyList<string>? CycleValues { get; private set; }
    /// <summary>Reads the current cycle value.</summary>
    public Func<string>? GetCycle { get; private set; }
    /// <summary>Writes a cycle value.</summary>
    public Action<string>? SetCycle { get; private set; }
    /// <summary>Inclusive slider minimum.</summary>
    public float Minimum { get; private set; }
    /// <summary>Inclusive slider maximum.</summary>
    public float Maximum { get; private set; }
    /// <summary>Slider increment, or zero for continuous movement.</summary>
    public float Step { get; private set; }
    /// <summary>Reads the slider value.</summary>
    public Func<float>? GetSlider { get; private set; }
    /// <summary>Writes the slider value.</summary>
    public Action<float>? SetSlider { get; private set; }
    /// <summary>Optional slider display formatter.</summary>
    public Func<float, string>? FormatSlider { get; private set; }
    /// <summary>Reads the RGB color.</summary>
    public Func<PluginColor>? GetColor { get; private set; }
    /// <summary>Writes the RGB color.</summary>
    public Action<PluginColor>? SetColor { get; private set; }

    /// <summary>Creates a Terraria-style enabled or disabled setting.</summary>
    public static PluginSettingControl Toggle(string id, string displayName, Func<bool> getValue, Action<bool> setValue)
    {
        var control = new PluginSettingControl(id, displayName, PluginSettingControlKind.Toggle) { GetToggle = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetToggle = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
        return control;
    }

    /// <summary>Creates a toggle directly bound to a host-owned typed setting.</summary>
    public static PluginSettingControl Toggle(string id, string displayName, IPluginSetting<bool> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Toggle(id, displayName, () => setting.Value, value => setting.Value = value);
    }

    /// <summary>Creates a Terraria-style cycling setting.</summary>
    public static PluginSettingControl Cycle(string id, string displayName, IReadOnlyList<string> values, Func<string> getValue, Action<string> setValue)
    {
        if (values == null || values.Count < 2 || values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("A cycle needs at least two non-empty values.", nameof(values));
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Cycle) { CycleValues = values.ToArray(), GetCycle = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetCycle = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
    }

    /// <summary>Creates a cycle directly bound to a host-owned string setting.</summary>
    public static PluginSettingControl Cycle(string id, string displayName, IReadOnlyList<string> values, IPluginSetting<string> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Cycle(id, displayName, values, () => setting.Value, value => setting.Value = value);
    }

    /// <summary>Creates a bounded Terraria-style numeric slider.</summary>
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, Func<float> getValue, Action<float> setValue, Func<float, string>? formatter = null)
    {
        if (float.IsNaN(minimum) || float.IsNaN(maximum) || minimum >= maximum || step < 0f) throw new ArgumentOutOfRangeException(nameof(minimum), "Slider bounds or step are invalid.");
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Slider) { Minimum = minimum, Maximum = maximum, Step = step, GetSlider = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetSlider = setValue ?? throw new ArgumentNullException(nameof(setValue)), FormatSlider = formatter };
    }

    /// <summary>Creates a slider directly bound to a host-owned float setting.</summary>
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, IPluginSetting<float> setting, Func<float, string>? formatter = null)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Slider(id, displayName, minimum, maximum, step, () => setting.Value, value => setting.Value = value, formatter);
    }

    /// <summary>Creates a discrete slider directly bound to a host-owned integer setting.</summary>
    public static PluginSettingControl Slider(string id, string displayName, float minimum, float maximum, float step, IPluginSetting<int> setting, Func<float, string>? formatter = null)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Slider(id, displayName, minimum, maximum, step, () => setting.Value, value => setting.Value = (int)Math.Round(value), formatter);
    }

    /// <summary>Creates a three-channel Terraria-style color picker with host-owned hexadecimal copy and paste.</summary>
    public static PluginSettingControl Color(string id, string displayName, Func<PluginColor> getValue, Action<PluginColor> setValue)
    {
        return new PluginSettingControl(id, displayName, PluginSettingControlKind.Color) { GetColor = getValue ?? throw new ArgumentNullException(nameof(getValue)), SetColor = setValue ?? throw new ArgumentNullException(nameof(setValue)) };
    }

    /// <summary>Creates a color control directly bound to a host-owned color setting.</summary>
    public static PluginSettingControl Color(string id, string displayName, IPluginSetting<PluginColor> setting)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Color(id, displayName, () => setting.Value, value => setting.Value = value);
    }

    /// <summary>Creates a color control bound to a legacy hexadecimal string setting without changing its persisted format.</summary>
    public static PluginSettingControl Color(string id, string displayName, IPluginSetting<string> setting, PluginColor defaultValue)
    {
        if (setting == null) throw new ArgumentNullException(nameof(setting));
        return Color(id, displayName, () => PluginColor.TryParseHex(setting.Value, out PluginColor value) ? value : defaultValue, value => setting.Value = value.ToHex());
    }

    /// <summary>Associates this retained control with one owner-local settings page.</summary>
    public PluginSettingControl InPage(string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId)) throw new ArgumentException("A settings page ID is required.", nameof(pageId));
        if (PageId != null && !string.Equals(PageId, pageId, StringComparison.Ordinal)) throw new InvalidOperationException("A setting control cannot be assigned to more than one page.");
        PageId = pageId;
        return this;
    }

}

/// <summary>Host-mediated service publication and discovery.</summary>
public interface IPluginServiceRegistry
{
    /// <summary>Publishes a contract implementation owned by the current plugin.</summary>
    IPluginRegistration Publish<TService>(TService service) where TService : class;

    /// <summary>Gets an active service contract without referencing its provider implementation.</summary>
    bool TryGet<TService>(out TService? service) where TService : class;

    /// <summary>Gets a declared dependency service or throws a clear availability error.</summary>
    TService GetRequired<TService>() where TService : class;
}

/// <summary>Read-only multiplayer session state supplied by the host.</summary>
public interface IMultiplayerSession
{
    /// <summary>Whether the client has an active multiplayer connection.</summary>
    bool IsConnected { get; }

    /// <summary>Whether the session remains compatible with vanilla servers.</summary>
    bool IsVanillaCompatibleMode { get; }

    /// <summary>Whether the connected server understands Alacrity policy negotiation.</summary>
    bool IsAlacrityAwareServer { get; }

    /// <summary>Current server identity, when connected.</summary>
    ServerIdentity? Server { get; }

    /// <summary>Current host-validated server policy, when available.</summary>
    ServerPluginPolicySnapshot? ActivePolicy { get; }
}

/// <summary>Read-only server identity.</summary>
public sealed class ServerIdentity
{
    /// <summary>Creates a server identity.</summary>
    public ServerIdentity(string address, string? displayName = null)
    {
        Address = string.IsNullOrWhiteSpace(address) ? throw new ArgumentException("A server address is required.", nameof(address)) : address;
        DisplayName = displayName;
    }

    /// <summary>Host and port used for the active session.</summary>
    public string Address { get; }

    /// <summary>Server-provided display name, when available.</summary>
    public string? DisplayName { get; }
}

/// <summary>Immutable effective policy state; desired user state never overrides a denial.</summary>
public sealed class ServerPluginPolicySnapshot
{
    /// <summary>Creates a policy snapshot.</summary>
    public ServerPluginPolicySnapshot(IReadOnlyCollection<PluginId> deniedPlugins)
    {
        DeniedPlugins = deniedPlugins ?? throw new ArgumentNullException(nameof(deniedPlugins));
    }

    /// <summary>Plugins denied by the active server policy.</summary>
    public IReadOnlyCollection<PluginId> DeniedPlugins { get; }

    /// <summary>Whether the policy denies a plugin.</summary>
    public bool IsDenied(PluginId pluginId)
    {
        foreach (var deniedPlugin in DeniedPlugins)
        {
            if (deniedPlugin == pluginId)
                return true;
        }

        return false;
    }
}
