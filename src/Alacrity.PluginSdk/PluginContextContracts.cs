using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// One host-owned registration that is released with its plugin resource scope.
public interface IPluginRegistration : IDisposable
{
    /// Stable diagnostic name for the registration.
    string Name { get; }

    /// Whether the host has released the registration.
    bool IsReleased { get; }
}

/// Asynchronous lifecycle for plugins loaded from a host-verified package manifest.
public interface IAsyncAlacrityPlugin
{
    /// Initializes plugin state from the host-supplied verified context.
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken);

    /// Activates registrations and runtime work.
    Task EnableAsync(CancellationToken cancellationToken);

    /// Stops runtime work before scope cleanup.
    Task DisableAsync(CancellationToken cancellationToken);

    /// Releases plugin-owned managed state.
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// Plugin-scoped typed settings boundary. Persistence and recovery are host-owned.
public interface IPluginSettings
{
    /// Registers a typed setting whose persistence is owned by the host.
    IPluginSetting<T> Register<T>(PluginSettingDefinition<T> definition);

    /// Gets a stored value or the supplied default.
    T Get<T>(string key, T defaultValue);

    /// Stores a validated setting value.
    void Set<T>(string key, T value);

    /// Removes a stored key.
    bool Remove(string key);

    /// Restores the plugin's registered default values.
    void ResetToDefaults();

    /// Raised after a setting changes.
    event EventHandler<PluginSettingChangedEventArgs> Changed;
}

/// Immutable declaration for one plugin-owned typed setting.
public sealed class PluginSettingDefinition<T>
{
    /// Creates a typed setting declaration with an optional normalizer.
    public PluginSettingDefinition(string key, T defaultValue, Func<T, T>? normalize = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        DefaultValue = defaultValue;
        Normalize = normalize;
    }

    /// Stable persisted key within the plugin namespace.
    public string Key { get; }
    /// Value returned when no valid persisted value exists.
    public T DefaultValue { get; }
    /// Optional host-applied normalization before values are exposed or persisted.
    public Func<T, T>? Normalize { get; }
}

/// Host-owned typed setting handle. Subscriptions are released with the owning plugin scope.
public interface IPluginSetting<T>
{
    /// Stable persisted key within the owning plugin namespace.
    string Key { get; }
    /// Declared default value.
    T DefaultValue { get; }
    /// Current normalized persisted value.
    T Value { get; set; }
    /// Restores the declared default value.
    void Reset();
    /// Subscribes to value changes with host-managed lifetime ownership.
    IPluginRegistration Subscribe(Action<T> handler);
}

/// Describes one plugin setting change.
public sealed class PluginSettingChangedEventArgs : EventArgs
{
    /// Creates a setting change notification.
    public PluginSettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("A setting key is required.", nameof(key)) : key;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// Changed key within the current plugin's settings namespace.
    public string Key { get; }

    /// Previous value, when one existed.
    public object? OldValue { get; }

    /// New value, when one exists.
    public object? NewValue { get; }
}

/// Path-confined storage for one plugin's data directory.
public interface IPluginStorage
{
    /// Opens a plugin-owned relative file for reading.
    Stream OpenRead(string relativePath);

    /// Creates or replaces a plugin-owned relative file.
    Stream Create(string relativePath);

    /// Checks a plugin-owned relative path.
    bool Exists(string relativePath);

    /// Deletes a plugin-owned relative file.
    void Delete(string relativePath);

    /// Lists paths beneath a plugin-owned relative directory.
    IReadOnlyList<string> Enumerate(string relativeDirectory);
}

/// Typed snapshot event subscriptions. Handlers run on the host-documented affinity for each event.
public interface IPluginEventService
{
    /// Subscribes a handler that is automatically removed when its resource scope is released.
    IPluginRegistration Subscribe<TEvent>(Action<TEvent> handler, PluginEventOptions? options = null);
}

/// Subscription delivery options.
public sealed class PluginEventOptions
{
    /// Whether host dispatch should stop this subscription after its first delivery.
    public bool Once { get; set; }
}

/// Registers validated plugin commands.
public interface IPluginCommandService
{
    /// Registers a command owned by the current plugin.
    IPluginRegistration Register(PluginCommandDescriptor descriptor, Action<PluginCommandInvocation> handler);
}

/// Explicit result of host command dispatch. A failed registered command is still consumed locally.
public enum PluginCommandDispatchResult
{
    /// No plugin owns the requested command.
    NotFound,
    /// A plugin command handled the invocation successfully.
    Handled,
    /// A plugin command was found and consumed but its callback failed.
    HandledWithFailure
}

/// Immutable command declaration.
public sealed class PluginCommandDescriptor
{
    /// Creates a command declaration.
    public PluginCommandDescriptor(string id, string helpText)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A command ID is required.", nameof(id)) : id;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? throw new ArgumentException("Help text is required.", nameof(helpText)) : helpText;
    }

    /// Stable command identifier within the current plugin.
    public string Id { get; }

    /// User-facing help text.
    public string HelpText { get; }
}

/// Validated command invocation arguments.
public sealed class PluginCommandInvocation
{
    /// Creates an invocation snapshot.
    public PluginCommandInvocation(IReadOnlyList<string> arguments, Action<string>? reply = null)
    {
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        this.reply = reply;
    }

    private readonly Action<string>? reply;

    /// Immutable argument list.
    public IReadOnlyList<string> Arguments { get; }

    /// Shows host-owned local feedback for this user-issued command when that UI is available.
    public void Reply(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A reply message is required.", nameof(message));
        reply?.Invoke(message);
    }
}

/// Registers user-rebindable plugin keybinds.
public interface IPluginKeybindService
{
    /// Registers a keybind owned by the current plugin.
    IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action handler);

    /// Registers a held keybind that receives true on press and false on release.
    IPluginRegistration Register(PluginKeybindDescriptor descriptor, Action<bool> stateHandler);
}

/// Determines whether a plugin keybind is invoked once or tracks its held state.
public enum PluginKeybindActivation
{
    /// Invokes the handler once for each fresh press.
    Press,
    /// Invokes the handler when the binding is pressed and released.
    Hold
}

/// Immutable keybind declaration.
public sealed class PluginKeybindDescriptor
{
    /// Creates a press-activated keybind declaration.
    public PluginKeybindDescriptor(string id, string defaultBinding, string displayName)
        : this(id, defaultBinding, displayName, PluginKeybindActivation.Press)
    {
    }

    /// Creates a keybind declaration with an explicit activation mode.
    public PluginKeybindDescriptor(string id, string defaultBinding, string displayName, PluginKeybindActivation activation)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A keybind ID is required.", nameof(id)) : id;
        DefaultBinding = string.IsNullOrWhiteSpace(defaultBinding) ? throw new ArgumentException("A default binding is required.", nameof(defaultBinding)) : defaultBinding;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName;
        if (!Enum.IsDefined(typeof(PluginKeybindActivation), activation)) throw new ArgumentOutOfRangeException(nameof(activation));
        Activation = activation;
    }

    /// Stable keybind identifier within the current plugin.
    public string Id { get; }

    /// Host-parseable default binding.
    public string DefaultBinding { get; }

    /// User-facing label.
    public string DisplayName { get; }

    /// Whether this binding activates once or follows its held state.
    public PluginKeybindActivation Activation { get; }
}

/// Immutable host-provided keybind row used by Terraria's controls-menu adapter.
public sealed class PluginKeybindRegistration
{
    /// Creates a row owned by a verified plugin package.
    public PluginKeybindRegistration(PluginId owner, string heading, PluginKeybindDescriptor descriptor)
        : this(owner, heading, descriptor, 0)
    {
    }

    /// Creates a row with host-owned monotonic registration ordering metadata.
    public PluginKeybindRegistration(PluginId owner, string heading, PluginKeybindDescriptor descriptor, long registrationSequence)
    {
        if (!owner.IsValid) throw new ArgumentException("A valid plugin owner is required.", nameof(owner));
        if (registrationSequence < 0) throw new ArgumentOutOfRangeException(nameof(registrationSequence));
        Owner = owner;
        Heading = string.IsNullOrWhiteSpace(heading) ? throw new ArgumentException("A heading is required.", nameof(heading)) : heading;
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        RegistrationSequence = registrationSequence;
    }

    /// Verified plugin package that owns this binding.
    public PluginId Owner { get; }
    /// Plugin heading appended after Terraria's built-in control groups.
    public string Heading { get; }
    /// Binding declaration and default.
    public PluginKeybindDescriptor Descriptor { get; }

    /// Host-owned monotonic ordering number; never reused after a registration is removed.
    public long RegistrationSequence { get; }

    /// Stable host key used by Terraria input-profile adapters. It is unique across plugin packages.
    public string HostId => Owner.Value + "." + Descriptor.Id;
}

/// Atomic immutable view of the host-owned keybind registry.
public sealed class PluginKeybindRegistrySnapshot
{
    /// Creates one snapshot returned under the host registry lock.
    public PluginKeybindRegistrySnapshot(long version, IReadOnlyList<PluginKeybindRegistration> registrations)
    {
        Version = version;
        Registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
    }

    /// Changes whenever any registration is added or removed.
    public long Version { get; }

    /// Registrations in deterministic owner and registration order.
    public IReadOnlyList<PluginKeybindRegistration> Registrations { get; }
}

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

/// Host-mediated service publication and discovery.
public interface IPluginServiceRegistry
{
    /// Publishes a contract implementation owned by the current plugin.
    IPluginRegistration Publish<TService>(TService service) where TService : class;

    /// Gets an active service contract without referencing its provider implementation.
    bool TryGet<TService>(out TService? service) where TService : class;

    /// Gets a declared dependency service or throws a clear availability error.
    TService GetRequired<TService>() where TService : class;
}

/// Read-only multiplayer session state supplied by the host.
public interface IMultiplayerSession
{
    /// Whether the client has an active multiplayer connection.
    bool IsConnected { get; }

    /// Whether the session remains compatible with vanilla servers.
    bool IsVanillaCompatibleMode { get; }

    /// Whether the connected server understands Alacrity policy negotiation.
    bool IsAlacrityAwareServer { get; }

    /// Current server identity, when connected.
    ServerIdentity? Server { get; }

    /// Current host-validated server policy, when available.
    ServerPluginPolicySnapshot? ActivePolicy { get; }
}

/// Read-only server identity.
public sealed class ServerIdentity
{
    /// Creates a server identity.
    public ServerIdentity(string address, string? displayName = null)
    {
        Address = string.IsNullOrWhiteSpace(address) ? throw new ArgumentException("A server address is required.", nameof(address)) : address;
        DisplayName = displayName;
    }

    /// Host and port used for the active session.
    public string Address { get; }

    /// Server-provided display name, when available.
    public string? DisplayName { get; }
}

/// Immutable effective policy state; desired user state never overrides a denial.
public sealed class ServerPluginPolicySnapshot
{
    /// Creates a policy snapshot.
    public ServerPluginPolicySnapshot(IReadOnlyCollection<PluginId> deniedPlugins)
    {
        DeniedPlugins = deniedPlugins ?? throw new ArgumentNullException(nameof(deniedPlugins));
    }

    /// Plugins denied by the active server policy.
    public IReadOnlyCollection<PluginId> DeniedPlugins { get; }

    /// Whether the policy denies a plugin.
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
