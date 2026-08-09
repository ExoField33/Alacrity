using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned registry for chat extensions. Terraria integration dispatches snapshots through this class.</summary>
public sealed class PluginChatHost
{
    private readonly object gate = new object();
    private readonly List<EditorEntry> editors = new List<EditorEntry>();
    private readonly List<DecoratorEntry> decorators = new List<DecoratorEntry>();
    private readonly List<FilterEntry> filters = new List<FilterEntry>();
    private readonly Dictionary<string, LinkEntry> links = new Dictionary<string, LinkEntry>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> lastFailures = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private EditorEntry[] editorSnapshot = Array.Empty<EditorEntry>();
    private DecoratorEntry[] decoratorSnapshot = Array.Empty<DecoratorEntry>();
    private FilterEntry[] filterSnapshot = Array.Empty<FilterEntry>();

    /// <summary>Fast-path state used by Terraria integration before entering a chat hook.</summary>
    public bool HasInputEditors => Volatile.Read(ref editorSnapshot).Length != 0;

    /// <summary>Returns whether a particular plugin owns an active chat editor.</summary>
    public bool HasInputEditor(PluginId owner)
    {
        if (!owner.IsValid) return false;
        foreach (EditorEntry entry in Volatile.Read(ref editorSnapshot))
            if (entry.Owner == owner) return true;
        return false;
    }

    /// <summary>Returns the first deterministic input-editor owner for host-mediated interaction operations.</summary>
    public bool TryGetActiveEditorOwner(out PluginId owner)
    {
        EditorEntry[] current = Volatile.Read(ref editorSnapshot);
        if (current.Length == 0) { owner = default; return false; }
        owner = current[0].Owner;
        return true;
    }

    /// <summary>Returns the interaction service attached to the deterministic active editor registration.</summary>
    public bool TryGetActiveEditorInteraction(out IPluginUserInteractionService? userInteraction)
    {
        EditorEntry[] current = Volatile.Read(ref editorSnapshot);
        if (current.Length == 0)
        {
            userInteraction = null;
            return false;
        }
        userInteraction = current[0].UserInteraction;
        return userInteraction != null;
    }

    /// <summary>Gets the activation-scoped interaction capability for the registration that produced a span.</summary>
    public bool TryGetInteraction(PluginId owner, out IPluginUserInteractionService? userInteraction)
    {
        foreach (DecoratorEntry entry in Volatile.Read(ref decoratorSnapshot))
            if (entry.Owner == owner && entry.IsAdmissionOpen && entry.UserInteraction != null) { userInteraction = entry.UserInteraction; return true; }
        foreach (EditorEntry entry in Volatile.Read(ref editorSnapshot))
            if (entry.Owner == owner && entry.IsAdmissionOpen && entry.UserInteraction != null) { userInteraction = entry.UserInteraction; return true; }
        userInteraction = null;
        return false;
    }

    /// <summary>Fast-path state used by Terraria integration before parsing presentation spans.</summary>
    public bool HasMessageDecorators => Volatile.Read(ref decoratorSnapshot).Length != 0;

    /// <summary>Fast-path state used before the host displays a classified chat message.</summary>
    public bool HasMessageFilters => Volatile.Read(ref filterSnapshot).Length != 0;

    /// <summary>Fast-path state used by Terraria integration before attempting external link activation.</summary>
    public bool HasLinkHandlers { get { lock (gate) return links.Count != 0; } }

    /// <summary>Creates a plugin-owned chat service set after manifest validation.</summary>
    public IPluginChatService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        return CreateService(manifest, resources, null);
    }

    /// <summary>Creates chat services bound to the owner's activation-scoped interaction capability.</summary>
    public IPluginChatService CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!manifest.Id.IsValid) throw new ArgumentException("Chat services require a valid plugin owner.", nameof(manifest));
        return new ScopedService(this, manifest, resources, userInteraction, ActivationCallbackGates.TryGet(resources));
    }

    public ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action)
    {
        EditorEntry[] current = Volatile.Read(ref editorSnapshot);
        foreach (var entry in current)
        {
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease)) continue;
            try
            {
                ChatInputEditResult result;
                using (lease)
                {
                    result = entry.Editor.Edit(snapshot, action) ?? ChatInputEditResult.Unhandled(snapshot);
                }
                if (result.Handled) return result;
            }
            catch (Exception exception) { ReportFailure(entry, "input editor", exception); entry.Dispose(); }
        }
        return ChatInputEditResult.Unhandled(snapshot);
    }

    public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
    {
        DecoratorEntry[] current = Volatile.Read(ref decoratorSnapshot);
        if (current.Length == 0)
            return new[] { new ChatTextSpan(message.Text) };

        IReadOnlyList<ChatTextSpan> spans = new[] { new ChatTextSpan(message.Text) };
        foreach (var entry in current)
        {
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease)) continue;
            try
            {
                string currentText = Concatenate(spans);
                IReadOnlyList<ChatTextSpan>? result;
                using (lease)
                {
                    result = entry.Decorator is IChatSpanDecorator composable
                        ? composable.Decorate(message, spans)
                        : entry.Decorator.Decorate(new ChatMessageSnapshot(currentText));
                }
                if (result != null && !IsIdentityDecoration(result, currentText))
                    spans = AssignOwner(result, entry.Owner);
            }
            catch (Exception exception) { ReportFailure(entry, "message decorator", exception); entry.Dispose(); }
        }
        return spans;
    }

    public bool ShouldDisplay(ChatMessageOrigin origin)
    {
        FilterEntry[] current = Volatile.Read(ref filterSnapshot);
        foreach (var entry in current)
        {
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease)) continue;
            try
            {
                using (lease)
                {
                    if (!entry.Filter.ShouldDisplay(origin)) return false;
                }
            }
            catch (Exception exception) { ReportFailure(entry, "message filter", exception); entry.Dispose(); }
        }
        return true;
    }

    public bool TryActivate(Uri uri)
    {
        if (uri == null) throw new ArgumentNullException(nameof(uri));
        LinkEntry entry;
        lock (gate) { if (!links.TryGetValue(uri.Scheme, out entry)) return false; }
        if (!entry.TryEnter(out ActivationCallbackGate.Lease lease)) return false;
        try { using (lease) { return entry.Handler.TryActivate(uri); } }
        catch (Exception exception) { ReportFailure(entry, "link handler", exception); entry.Dispose(); return false; }
    }

    private IPluginRegistration RegisterEditor(PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction, ChatInputEditorDescriptor descriptor, IChatInputEditor editor)
    {
        EnsureCapability(manifest, PluginCapability.Input, "chat input editor");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (editor == null) throw new ArgumentNullException(nameof(editor));
        var entry = new EditorEntry(manifest.Id, descriptor, editor, userInteraction, RemoveEditor, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-editor:" + descriptor.Id);
        lock (gate) { if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope"); editors.Add(entry); RebuildEditorSnapshot(); }
        return entry;
    }
    private IPluginRegistration RegisterDecorator(PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction, ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator)
    {
        EnsureUserInterfaceAccess(manifest, "chat message decorator");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (decorator == null) throw new ArgumentNullException(nameof(decorator));
        var entry = new DecoratorEntry(manifest.Id, descriptor, decorator, userInteraction, RemoveDecorator, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-decorator:" + descriptor.Id);
        lock (gate) { if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope"); decorators.Add(entry); RebuildDecoratorSnapshot(); }
        return entry;
    }
    private IPluginRegistration RegisterFilter(PluginManifest manifest, IPluginResourceScope resources, ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter)
    {
        EnsureUserInterfaceAccess(manifest, "chat message filter");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (filter == null) throw new ArgumentNullException(nameof(filter));
        var entry = new FilterEntry(manifest.Id, descriptor, filter, RemoveFilter, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-filter:" + descriptor.Id);
        lock (gate) { if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope"); filters.Add(entry); RebuildFilterSnapshot(); }
        return entry;
    }
    private IPluginRegistration RegisterLink(PluginManifest manifest, IPluginResourceScope resources, ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler)
    {
        EnsureUserInterfaceAccess(manifest, "chat link handler");
        EnsurePermission(manifest, PluginPermission.OpenExternalLinks, "chat link handler");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var entry = new LinkEntry(manifest.Id, descriptor.Scheme, handler, RemoveLink, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-link:" + descriptor.Scheme);
        bool duplicate;
        lock (gate)
        {
            if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope");
            duplicate = links.ContainsKey(descriptor.Scheme);
            if (!duplicate) links.Add(descriptor.Scheme, entry);
        }
        if (duplicate) { entry.Dispose(); throw new InvalidOperationException("A chat link handler is already registered for " + descriptor.Scheme + "."); }
        return entry;
    }
    private static IPluginRegistration Own(IPluginResourceScope scope, IPluginRegistration registration, string name)
    {
        try { scope.Own(name, PluginResourceKind.UserInterface, registration); }
        catch { registration.Dispose(); throw; }
        return registration;
    }
    private void RemoveEditor(EditorEntry entry) { lock (gate) { editors.Remove(entry); RebuildEditorSnapshot(); } }
    private void RemoveDecorator(DecoratorEntry entry) { lock (gate) { decorators.Remove(entry); RebuildDecoratorSnapshot(); } }
    private void RemoveFilter(FilterEntry entry) { lock (gate) { filters.Remove(entry); RebuildFilterSnapshot(); } }
    private void RemoveLink(LinkEntry entry) { lock (gate) if (links.TryGetValue(entry.Scheme, out var current) && ReferenceEquals(current, entry)) links.Remove(entry.Scheme); }

    // Failures are isolated at the host boundary; diagnostics are throttled so malformed chat cannot flood logs.
    private void ReportFailure(Entry entry, string callbackType, Exception exception)
    {
        string key = entry.Owner.Value + ":" + entry.Name;
        lock (gate)
        {
            DateTime now = DateTime.UtcNow;
            if (lastFailures.TryGetValue(key, out DateTime previous) && now - previous < TimeSpan.FromSeconds(10)) return;
            lastFailures[key] = now;
        }
        Trace.TraceError("Alacrity chat " + callbackType + " failed for plugin '" + entry.Owner + "', registration '" + entry.Name + "': " + exception);
    }

    private static bool IsIdentityDecoration(IReadOnlyList<ChatTextSpan> spans, string text)
    {
        return spans.Count == 1 && spans[0].LinkTarget == null && string.Equals(spans[0].Text, text, StringComparison.Ordinal);
    }

    private static string Concatenate(IReadOnlyList<ChatTextSpan> spans)
    {
        if (spans.Count == 0)
            return string.Empty;
        if (spans.Count == 1)
            return spans[0].Text;

        var text = new System.Text.StringBuilder();
        for (int index = 0; index < spans.Count; index++)
            text.Append(spans[index].Text);
        return text.ToString();
    }
    private static IReadOnlyList<ChatTextSpan> AssignOwner(IReadOnlyList<ChatTextSpan> spans, PluginId owner)
    {
        var owned = new ChatTextSpan[spans.Count];
        for (int index = 0; index < spans.Count; index++)
            owned[index] = new ChatTextSpan(spans[index].Text, spans[index].LinkTarget, owner);
        return owned;
    }

    private void RebuildEditorSnapshot() => Volatile.Write(ref editorSnapshot, editors.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private void RebuildDecoratorSnapshot() => Volatile.Write(ref decoratorSnapshot, decorators.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private void RebuildFilterSnapshot() => Volatile.Write(ref filterSnapshot, filters.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private static void EnsureCapability(PluginManifest manifest, PluginCapability capability, string service)
    {
        if ((manifest.Capabilities & capability) != capability)
            throw new UnauthorizedAccessException("Plugin '" + manifest.Id + "' must declare " + capability + " to register a " + service + ".");
    }
    private static void EnsurePermission(PluginManifest manifest, PluginPermission permission, string service)
    {
        if ((manifest.Permissions & permission) != permission)
            throw new UnauthorizedAccessException("Plugin '" + manifest.Id + "' must declare " + permission + " to register a " + service + ".");
    }
    private static void EnsureUserInterfaceAccess(PluginManifest manifest, string service)
    {
        EnsureCapability(manifest, PluginCapability.UserInterface, service);
        EnsurePermission(manifest, PluginPermission.DrawUserInterface, service);
    }

    private sealed class ScopedService : IPluginChatService
    {
        private readonly PluginChatHost host; private readonly PluginManifest manifest; private readonly IPluginResourceScope resources; private readonly IPluginUserInteractionService? userInteraction; private readonly ActivationCallbackGate? callbackGate;
        public ScopedService(PluginChatHost host, PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction, ActivationCallbackGate? callbackGate) { this.host = host; this.manifest = manifest; this.resources = resources; this.userInteraction = userInteraction; this.callbackGate = callbackGate; }
        public IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor) { ThrowIfClosed(); return host.RegisterEditor(manifest, resources, userInteraction, descriptor, editor); }
        public IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator) { ThrowIfClosed(); return host.RegisterDecorator(manifest, resources, userInteraction, descriptor, decorator); }
        public IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter) { ThrowIfClosed(); return host.RegisterFilter(manifest, resources, descriptor, filter); }
        public IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler) { ThrowIfClosed(); return host.RegisterLink(manifest, resources, descriptor, handler); }
        private void ThrowIfClosed() { if (callbackGate != null && callbackGate.IsClosed) throw new ObjectDisposedException("Plugin activation"); }
    }
    private abstract class Entry : IPluginRegistration { private readonly Action<Entry> remove; private readonly ActivationCallbackGate? callbackGate; private bool released; protected Entry(PluginId owner, string name, Action<Entry> remove, ActivationCallbackGate? callbackGate) { Owner = owner; Name = name; this.remove = remove; this.callbackGate = callbackGate; } public PluginId Owner { get; } public string Name { get; } public bool IsReleased => released; public bool IsAdmissionOpen => !released && (callbackGate == null || !callbackGate.IsClosed); public bool TryEnter(out ActivationCallbackGate.Lease lease) { if (IsReleased) { lease = default; return false; } if (callbackGate == null) { lease = default; return true; } return callbackGate.TryEnter(out lease); } public void Dispose() { if (released) return; released = true; remove(this); } }
    private sealed class EditorEntry : Entry { public EditorEntry(PluginId owner, ChatInputEditorDescriptor descriptor, IChatInputEditor editor, IPluginUserInteractionService? userInteraction, Action<EditorEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-editor:" + descriptor.Id, entry => remove((EditorEntry)entry), callbackGate) { Descriptor = descriptor; Editor = editor; UserInteraction = userInteraction; } public ChatInputEditorDescriptor Descriptor { get; } public IChatInputEditor Editor { get; } public IPluginUserInteractionService? UserInteraction { get; } }
    private sealed class DecoratorEntry : Entry { public DecoratorEntry(PluginId owner, ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator, IPluginUserInteractionService? userInteraction, Action<DecoratorEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-decorator:" + descriptor.Id, entry => remove((DecoratorEntry)entry), callbackGate) { Descriptor = descriptor; Decorator = decorator; UserInteraction = userInteraction; } public ChatMessageDecoratorDescriptor Descriptor { get; } public IChatMessageDecorator Decorator { get; } public IPluginUserInteractionService? UserInteraction { get; } }
    private sealed class FilterEntry : Entry { public FilterEntry(PluginId owner, ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter, Action<FilterEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-filter:" + descriptor.Id, entry => remove((FilterEntry)entry), callbackGate) { Descriptor = descriptor; Filter = filter; } public ChatMessageFilterDescriptor Descriptor { get; } public IChatMessageFilter Filter { get; } }
    private sealed class LinkEntry : Entry { public LinkEntry(PluginId owner, string scheme, IChatLinkHandler handler, Action<LinkEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-link:" + scheme, entry => remove((LinkEntry)entry), callbackGate) { Scheme = scheme; Handler = handler; } public string Scheme { get; } public IChatLinkHandler Handler { get; } }
}

/// <summary>Concrete Terraria service grouping assembled by the host.</summary>
public sealed class PluginTerrariaServices : ITerrariaServices
{
    public PluginTerrariaServices(IPluginChatService chat, IPluginEntitySnapshotService? entities = null, IPluginVisualEffectsService? visualEffects = null, IPluginPlayerService? players = null, IPluginSessionPresentationService? session = null)
    {
        Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Entities = entities ?? EmptyPluginEntitySnapshotService.Instance;
        VisualEffects = visualEffects ?? EmptyPluginVisualEffectsService.Instance;
        Players = players ?? EmptyPluginPlayerService.Instance;
        Session = session ?? EmptyPluginSessionPresentationService.Instance;
    }
    public IPluginChatService Chat { get; }
    public IPluginEntitySnapshotService Entities { get; }
    public IPluginVisualEffectsService VisualEffects { get; }
    public IPluginPlayerService Players { get; }
    public IPluginSessionPresentationService Session { get; }
}

internal sealed class EmptyPluginSessionPresentationService : IPluginSessionPresentationService
{
    internal static readonly EmptyPluginSessionPresentationService Instance = new EmptyPluginSessionPresentationService();
    private EmptyPluginSessionPresentationService() { }
    public PluginSessionPresentationSnapshot GetCurrent() => default;
}

internal sealed class EmptyPluginPlayerService : IPluginPlayerService
{
    internal static readonly EmptyPluginPlayerService Instance = new EmptyPluginPlayerService();
    private EmptyPluginPlayerService() { }
    public int ActivePlayerCount => 0;
    public bool TryGet(int playerId, out PluginPlayerSnapshot player) { player = default; return false; }
    public bool TryGet(PluginEntityHandle handle, out PluginPlayerSnapshot player) { player = default; return false; }
    public string? GetName(int playerId) => null;
    public void CopyPlayers(System.Collections.Generic.ICollection<PluginPlayerSnapshot> destination) { if (destination == null) throw new ArgumentNullException(nameof(destination)); }
    public void CopyBuffs(int playerId, System.Collections.Generic.ICollection<PluginBuffSnapshot> destination) { if (destination == null) throw new ArgumentNullException(nameof(destination)); }
}

internal sealed class EmptyPluginVisualEffectsService : IPluginVisualEffectsService
{
    internal static readonly EmptyPluginVisualEffectsService Instance = new EmptyPluginVisualEffectsService();
    private EmptyPluginVisualEffectsService() { }
    public IPluginRegistration RegisterPolicy(PluginVisualEffectsPolicy policy) => throw new NotSupportedException("Visual-effects policies are unavailable in this host.");
}

internal sealed class EmptyPluginEntitySnapshotService : IPluginEntitySnapshotService, IPluginMeleeCollisionSnapshotService
{
    internal static readonly EmptyPluginEntitySnapshotService Instance = new EmptyPluginEntitySnapshotService();
    private EmptyPluginEntitySnapshotService() { }
    public int ActiveEntityCount => 0;
    public void CopyActiveEntities(System.Collections.Generic.ICollection<PluginEntitySnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
    }
    public void CopyMeleeHitboxes(System.Collections.Generic.ICollection<PluginEntitySnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
    }
    public bool TryGetBySlot(PluginEntityKind kind, int slot, out PluginEntitySnapshot entity) { entity = default; return false; }
    public bool TryGetByHandle(PluginEntityHandle handle, out PluginEntitySnapshot entity) { entity = default; return false; }
    public IPluginRegistration RequestMeleeCollisionSnapshots() => throw new NotSupportedException("Melee collision snapshots are unavailable in this host.");
}
