using System;

namespace Alacrity.PluginSdk;

/// <summary>Stable positions used by the Alacrity main-menu layout.</summary>
public enum MainMenuEntryId
{
    /// <summary>Single-player entry.</summary>
    SinglePlayer,
    /// <summary>Multiplayer entry.</summary>
    Multiplayer,
    /// <summary>Achievements entry.</summary>
    Achievements,
    /// <summary>Plugin management entry.</summary>
    Plugins,
    /// <summary>Original Workshop entry shifted below Plugins.</summary>
    Workshop,
    /// <summary>Settings entry.</summary>
    Settings,
    /// <summary>Credits entry.</summary>
    Credits,
    /// <summary>Exit entry.</summary>
    Exit
}

/// <summary>One main-menu entry and its resolved order.</summary>
public sealed class MainMenuEntry
{
    /// <summary>Creates a main-menu entry.</summary>
    public MainMenuEntry(MainMenuEntryId id, string label, int order)
    {
        Id = id;
        Label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("A label is required.", nameof(label)) : label;
        Order = order;
    }

    /// <summary>Stable entry identity.</summary>
    public MainMenuEntryId Id { get; }
    /// <summary>Displayed entry text.</summary>
    public string Label { get; }
    /// <summary>Zero-based display order.</summary>
    public int Order { get; }
}

/// <summary>Plugin row state used by a Workshop-style plugin settings screen.</summary>
public sealed class PluginSettingsEntry
{
    /// <summary>Creates a plugin settings row.</summary>
    public PluginSettingsEntry(
        PluginId id,
        string name,
        string author,
        Version version,
        string description,
        string changelog,
        PluginLifecycleState state,
        bool canConfigure)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A name is required.", nameof(name)) : name;
        Author = string.IsNullOrWhiteSpace(author) ? throw new ArgumentException("An author is required.", nameof(author)) : author;
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Description = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("A description is required.", nameof(description)) : description;
        Changelog = string.IsNullOrWhiteSpace(changelog) ? throw new ArgumentException("A changelog is required.", nameof(changelog)) : changelog;
        State = state;
        CanConfigure = canConfigure;
    }

    /// <summary>Plugin identifier.</summary>
    public PluginId Id { get; }
    /// <summary>Displayed plugin name.</summary>
    public string Name { get; }
    /// <summary>Plugin author or publisher.</summary>
    public string Author { get; }
    /// <summary>Plugin package version.</summary>
    public Version Version { get; }
    /// <summary>Plugin description displayed by the manager.</summary>
    public string Description { get; }
    /// <summary>Plugin release notes displayed by the manager.</summary>
    public string Changelog { get; }
    /// <summary>Current lifecycle state.</summary>
    public PluginLifecycleState State { get; }
    /// <summary>Whether the row has a settings action.</summary>
    public bool CanConfigure { get; }
    /// <summary>Whether the row can currently be toggled.</summary>
    public bool CanToggle => State == PluginLifecycleState.Disabled || State == PluginLifecycleState.Enabled;
    /// <summary>Whether the plugin is currently active.</summary>
    public bool IsEnabled => State == PluginLifecycleState.Enabled;
}
