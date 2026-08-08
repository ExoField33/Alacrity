using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

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

