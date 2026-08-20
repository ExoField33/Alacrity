using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned registry for chat extensions. Terraria integration dispatches snapshots through this class.</summary>
public sealed partial class PluginChatHost
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
    private MessageActionEntry[] messageActionSnapshot = Array.Empty<MessageActionEntry>();
    private ChatActionButtonRegistrySnapshot actionButtonSnapshot = ChatActionButtonRegistrySnapshot.Empty;
    private OutgoingTransformerEntry[] outgoingTransformerSnapshot = Array.Empty<OutgoingTransformerEntry>();
    private readonly Dictionary<long, MessagePresentationEntry> presentations = new Dictionary<long, MessagePresentationEntry>();
    private PendingOutgoingMessage? pendingOutgoing;
    private string? readyOutgoingSubmission;
    private string? readyOutgoingSource;
    private PluginId readyOutgoingOwner;

    /// <summary>
    /// Raised after an owner-validated retained-message presentation changes. Integration
    /// adapters use this only to request a later native presentation refresh; plugin callbacks
    /// are never invoked through this notification.
    /// </summary>
    public event Action<ChatMessageHandle>? MessagePresentationUpdated;

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
        return CreateService(manifest, resources, userInteraction, null, null);
    }

    /// <summary>Creates scoped chat services with the activation-owned scheduler used by asynchronous outgoing transforms.</summary>
    public IPluginChatService CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction, IPluginScheduler? scheduler, IPluginLogger? logger)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!manifest.Id.IsValid) throw new ArgumentException("Chat services require a valid plugin owner.", nameof(manifest));
        return new ScopedService(this, manifest, resources, userInteraction, scheduler, logger, ActivationCallbackGates.TryGet(resources));
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

    /// <summary>
    /// Returns whether an active editor currently owns an action Terraria would otherwise process
    /// before the generic editor pipeline runs.
    /// </summary>
    public bool HasInputActionHandler(ChatInputAction action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        EditorEntry[] current = Volatile.Read(ref editorSnapshot);
        foreach (EditorEntry entry in current)
        {
            if (!(entry.Editor is IChatInputActionAvailability availability) || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
                continue;

            try
            {
                using (lease)
                {
                    if (availability.CanHandle(action))
                        return true;
                }
            }
            catch (Exception exception)
            {
                ReportFailure(entry, "input-action availability", exception);
                entry.Dispose();
            }
        }

        return false;
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
        if (message.Handle.IsValid)
        {
            PluginId owner = default;
            for (int index = 0; index < spans.Count; index++)
            {
                if (!string.IsNullOrEmpty(spans[index].ActionId) && spans[index].Owner.IsValid)
                {
                    owner = spans[index].Owner;
                    break;
                }
            }

            if (owner.IsValid)
            {
                lock (gate)
                {
                    // A native chat container can rebuild its wrapped snippets after a retained
                    // presentation changes. Keep the owner-validated replacement while that
                    // rebuild reuses the same message handle.
                    if (!presentations.TryGetValue(message.Handle.Value, out MessagePresentationEntry? existing) ||
                        existing.Owner != owner ||
                        existing.SpanCount != spans.Count)
                    {
                        presentations[message.Handle.Value] = new MessagePresentationEntry(owner, spans.Count);
                    }
                    TrimPresentations();
                }
            }
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

    private IPluginRegistration RegisterMessageAction(PluginManifest manifest, IPluginResourceScope resources, ChatMessageActionDescriptor descriptor, IChatMessageActionHandler handler)
    {
        EnsureUserInterfaceAccess(manifest, "chat message action");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var entry = new MessageActionEntry(manifest.Id, descriptor, handler, RemoveMessageAction, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-message-action:" + descriptor.Id);
        lock (gate)
        {
            if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope");
            for (int index = 0; index < messageActions.Count; index++)
                if (messageActions[index].Owner == manifest.Id && string.Equals(messageActions[index].Descriptor.Id, descriptor.Id, StringComparison.Ordinal))
                {
                    entry.Dispose();
                    throw new InvalidOperationException("A chat message action is already registered for '" + descriptor.Id + "'.");
                }
            messageActions.Add(entry);
            RebuildMessageActionSnapshot();
        }
        return entry;
    }

    private IPluginRegistration RegisterActionButton(PluginManifest manifest, IPluginResourceScope resources, ChatActionButtonDescriptor descriptor, IChatActionButtonHandler handler)
    {
        EnsureUserInterfaceAccess(manifest, "chat action button");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var entry = new ChatActionButtonEntry(manifest.Id, descriptor, handler, RemoveActionButton, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-action-button:" + descriptor.Id);
        lock (gate)
        {
            if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope");
            for (int index = 0; index < actionButtons.Count; index++)
                if (actionButtons[index].Owner == manifest.Id && string.Equals(actionButtons[index].Descriptor.Id, descriptor.Id, StringComparison.Ordinal))
                {
                    entry.Dispose();
                    throw new InvalidOperationException("A chat action button is already registered for '" + descriptor.Id + "'.");
                }
            actionButtons.Add(entry);
            RebuildActionButtonSnapshot();
        }
        return entry;
    }

    private IPluginRegistration RegisterOutgoingTransformer(PluginManifest manifest, IPluginResourceScope resources, IPluginScheduler? scheduler, IPluginLogger? logger, ChatOutgoingMessageTransformerDescriptor descriptor, IChatOutgoingMessageTransformer transformer)
    {
        EnsureCapability(manifest, PluginCapability.Networking, "outgoing chat transformer");
        if (scheduler == null || logger == null) throw new NotSupportedException("This host does not provide activation-scoped background scheduling for outgoing chat transforms.");
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (transformer == null) throw new ArgumentNullException(nameof(transformer));
        var entry = new OutgoingTransformerEntry(manifest.Id, descriptor, transformer, scheduler, logger, RemoveOutgoingTransformer, ActivationCallbackGates.TryGet(resources));
        Own(resources, entry, "chat-outgoing-transformer:" + descriptor.Id);
        lock (gate)
        {
            if (entry.IsReleased) throw new ObjectDisposedException("IPluginResourceScope");
            for (int index = 0; index < outgoingTransformers.Count; index++)
                if (outgoingTransformers[index].Owner == manifest.Id && string.Equals(outgoingTransformers[index].Descriptor.Id, descriptor.Id, StringComparison.Ordinal))
                {
                    entry.Dispose();
                    throw new InvalidOperationException("An outgoing chat transformer is already registered for '" + descriptor.Id + "'.");
                }
            outgoingTransformers.Add(entry);
            RebuildOutgoingTransformerSnapshot();
        }
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
    private void RemoveMessageAction(MessageActionEntry entry) { lock (gate) { messageActions.Remove(entry); RemovePresentations(entry.Owner); RebuildMessageActionSnapshot(); } }
    private void RemoveActionButton(ChatActionButtonEntry entry) { lock (gate) { actionButtons.Remove(entry); RebuildActionButtonSnapshot(); } }
    private void RemoveOutgoingTransformer(OutgoingTransformerEntry entry)
    {
        PendingOutgoingMessage? cancelled = null;
        lock (gate)
        {
            outgoingTransformers.Remove(entry);
            if (pendingOutgoing != null && ReferenceEquals(pendingOutgoing.Entry, entry))
            {
                cancelled = pendingOutgoing;
                pendingOutgoing = null;
            }

            if (readyOutgoingOwner == entry.Owner)
            {
                readyOutgoingSubmission = null;
                readyOutgoingSource = null;
                readyOutgoingOwner = default;
            }

            RebuildOutgoingTransformerSnapshot();
        }

        cancelled?.Cancel();
    }

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
            owned[index] = new ChatTextSpan(spans[index].Text, spans[index].LinkTarget, spans[index].ActionId, spans[index].ActionTarget, spans[index].Color, owner);
        return owned;
    }

    private void RebuildEditorSnapshot() => Volatile.Write(ref editorSnapshot, editors.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private void RebuildDecoratorSnapshot() => Volatile.Write(ref decoratorSnapshot, decorators.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private void RebuildFilterSnapshot() => Volatile.Write(ref filterSnapshot, filters.OrderBy(entry => entry.Descriptor.Priority).ToArray());
    private void RebuildMessageActionSnapshot() => Volatile.Write(ref messageActionSnapshot, messageActions.ToArray());
    private void RebuildActionButtonSnapshot()
    {
        ChatActionButtonEntry[] entries = actionButtons.OrderBy(entry => entry.Descriptor.Priority).ToArray();
        var views = new ChatActionButtonView[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            views[index] = new ChatActionButtonView(entries[index].Owner, entries[index].Descriptor);
        }

        Volatile.Write(ref actionButtonSnapshot, new ChatActionButtonRegistrySnapshot(entries, views));
    }
    private void RebuildOutgoingTransformerSnapshot() => Volatile.Write(ref outgoingTransformerSnapshot, outgoingTransformers.OrderBy(entry => entry.Descriptor.Priority).ToArray());
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
        private readonly PluginChatHost host; private readonly PluginManifest manifest; private readonly IPluginResourceScope resources; private readonly IPluginUserInteractionService? userInteraction; private readonly IPluginScheduler? scheduler; private readonly IPluginLogger? logger; private readonly ActivationCallbackGate? callbackGate;
        public ScopedService(PluginChatHost host, PluginManifest manifest, IPluginResourceScope resources, IPluginUserInteractionService? userInteraction, IPluginScheduler? scheduler, IPluginLogger? logger, ActivationCallbackGate? callbackGate) { this.host = host; this.manifest = manifest; this.resources = resources; this.userInteraction = userInteraction; this.scheduler = scheduler; this.logger = logger; this.callbackGate = callbackGate; }
        public IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor) { ThrowIfClosed(); return host.RegisterEditor(manifest, resources, userInteraction, descriptor, editor); }
        public IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator) { ThrowIfClosed(); return host.RegisterDecorator(manifest, resources, userInteraction, descriptor, decorator); }
        public IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter) { ThrowIfClosed(); return host.RegisterFilter(manifest, resources, descriptor, filter); }
        public IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler) { ThrowIfClosed(); return host.RegisterLink(manifest, resources, descriptor, handler); }
        public IPluginRegistration RegisterMessageAction(ChatMessageActionDescriptor descriptor, IChatMessageActionHandler handler) { ThrowIfClosed(); return host.RegisterMessageAction(manifest, resources, descriptor, handler); }
        public IPluginRegistration RegisterActionButton(ChatActionButtonDescriptor descriptor, IChatActionButtonHandler handler) { ThrowIfClosed(); return host.RegisterActionButton(manifest, resources, descriptor, handler); }
        public IPluginRegistration RegisterOutgoingMessageTransformer(ChatOutgoingMessageTransformerDescriptor descriptor, IChatOutgoingMessageTransformer transformer) { ThrowIfClosed(); return host.RegisterOutgoingTransformer(manifest, resources, scheduler, logger, descriptor, transformer); }
        public bool TryUpdateMessagePresentation(ChatMessageHandle message, ChatMessagePresentation presentation) { ThrowIfClosed(); return host.TryUpdateMessagePresentation(manifest.Id, message, presentation); }
        private void ThrowIfClosed() { if (callbackGate != null && callbackGate.IsClosed) throw new ObjectDisposedException("Plugin activation"); }
    }
    private abstract class Entry : IPluginRegistration
    {
        private readonly Action<Entry> remove;
        private readonly ActivationCallbackGate? callbackGate;
        private int released;

        protected Entry(PluginId owner, string name, Action<Entry> remove, ActivationCallbackGate? callbackGate)
        {
            Owner = owner;
            Name = name;
            this.remove = remove;
            this.callbackGate = callbackGate;
        }

        public PluginId Owner { get; }
        public string Name { get; }
        public bool IsReleased => Volatile.Read(ref released) != 0;
        public bool IsAdmissionOpen => !IsReleased && (callbackGate == null || !callbackGate.IsClosed);

        public bool TryEnter(out ActivationCallbackGate.Lease lease)
        {
            if (IsReleased)
            {
                lease = default;
                return false;
            }

            if (callbackGate == null)
            {
                lease = default;
                return true;
            }

            return callbackGate.TryEnter(out lease);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            remove(this);
        }
    }
    private sealed class EditorEntry : Entry { public EditorEntry(PluginId owner, ChatInputEditorDescriptor descriptor, IChatInputEditor editor, IPluginUserInteractionService? userInteraction, Action<EditorEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-editor:" + descriptor.Id, entry => remove((EditorEntry)entry), callbackGate) { Descriptor = descriptor; Editor = editor; UserInteraction = userInteraction; } public ChatInputEditorDescriptor Descriptor { get; } public IChatInputEditor Editor { get; } public IPluginUserInteractionService? UserInteraction { get; } }
    private sealed class DecoratorEntry : Entry { public DecoratorEntry(PluginId owner, ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator, IPluginUserInteractionService? userInteraction, Action<DecoratorEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-decorator:" + descriptor.Id, entry => remove((DecoratorEntry)entry), callbackGate) { Descriptor = descriptor; Decorator = decorator; UserInteraction = userInteraction; } public ChatMessageDecoratorDescriptor Descriptor { get; } public IChatMessageDecorator Decorator { get; } public IPluginUserInteractionService? UserInteraction { get; } }
    private sealed class FilterEntry : Entry { public FilterEntry(PluginId owner, ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter, Action<FilterEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-filter:" + descriptor.Id, entry => remove((FilterEntry)entry), callbackGate) { Descriptor = descriptor; Filter = filter; } public ChatMessageFilterDescriptor Descriptor { get; } public IChatMessageFilter Filter { get; } }
    private sealed class LinkEntry : Entry { public LinkEntry(PluginId owner, string scheme, IChatLinkHandler handler, Action<LinkEntry> remove, ActivationCallbackGate? callbackGate) : base(owner, "chat-link:" + scheme, entry => remove((LinkEntry)entry), callbackGate) { Scheme = scheme; Handler = handler; } public string Scheme { get; } public IChatLinkHandler Handler { get; } }

    private sealed class ChatActionButtonRegistrySnapshot
    {
        internal static readonly ChatActionButtonRegistrySnapshot Empty = new ChatActionButtonRegistrySnapshot(Array.Empty<ChatActionButtonEntry>(), Array.Empty<ChatActionButtonView>());

        internal ChatActionButtonRegistrySnapshot(ChatActionButtonEntry[] entries, ChatActionButtonView[] views)
        {
            Entries = entries;
            Views = views;
        }

        internal ChatActionButtonEntry[] Entries { get; }
        internal ChatActionButtonView[] Views { get; }
    }
}

/// <summary>Concrete Terraria service grouping assembled by the host.</summary>
public sealed class PluginTerrariaServices : ITerrariaServices
{
    public PluginTerrariaServices(IPluginChatService chat, IPluginEntitySnapshotService? entities = null, IPluginVisualEffectsService? visualEffects = null, IPluginPlayerService? players = null, IPluginSessionPresentationService? session = null, IPluginNpcTargetSnapshotService? npcTargets = null, IPluginWorldSectionService? worldSections = null, IPluginRenderCullingService? renderCulling = null, IPluginRenderingOptimizationService? renderingOptimizations = null, IPluginPresentationSuppressionService? presentation = null)
    {
        Chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Entities = entities ?? EmptyPluginEntitySnapshotService.Instance;
        VisualEffects = visualEffects ?? EmptyPluginVisualEffectsService.Instance;
        Players = players ?? EmptyPluginPlayerService.Instance;
        Session = session ?? EmptyPluginSessionPresentationService.Instance;
        NpcTargets = npcTargets ?? EmptyPluginNpcTargetSnapshotService.Instance;
        WorldSections = worldSections ?? EmptyPluginWorldSectionService.Instance;
        RenderCulling = renderCulling ?? EmptyPluginRenderCullingService.Instance;
        RenderingOptimizations = renderingOptimizations ?? EmptyPluginRenderingOptimizationService.Instance;
        Presentation = presentation ?? EmptyPluginPresentationSuppressionService.Instance;
    }
    public IPluginChatService Chat { get; }
    public IPluginEntitySnapshotService Entities { get; }
    public IPluginVisualEffectsService VisualEffects { get; }
    public IPluginPlayerService Players { get; }
    public IPluginSessionPresentationService Session { get; }
    public IPluginNpcTargetSnapshotService NpcTargets { get; }
    public IPluginWorldSectionService WorldSections { get; }
    public IPluginRenderCullingService RenderCulling { get; }
    public IPluginRenderingOptimizationService RenderingOptimizations { get; }
    public IPluginPresentationSuppressionService Presentation { get; }
}

internal sealed class EmptyPluginPresentationSuppressionService : IPluginPresentationSuppressionService
{
    internal static readonly EmptyPluginPresentationSuppressionService Instance = new EmptyPluginPresentationSuppressionService();

    private EmptyPluginPresentationSuppressionService()
    {
    }

    public IPluginRegistration RegisterPolicy(PluginPresentationSuppressionPolicy policy)
    {
        throw new NotSupportedException("Presentation suppression is unavailable in this host.");
    }
}

internal sealed class EmptyPluginRenderingOptimizationService : IPluginRenderingOptimizationService
{
    internal static readonly EmptyPluginRenderingOptimizationService Instance = new EmptyPluginRenderingOptimizationService();

    private EmptyPluginRenderingOptimizationService()
    {
    }

    public IPluginRegistration RegisterPolicy(PluginRenderingOptimizationPolicy policy)
    {
        throw new NotSupportedException("Rendering optimizations are unavailable in this host.");
    }
}

internal sealed class EmptyPluginRenderCullingService : IPluginRenderCullingService
{
    internal static readonly EmptyPluginRenderCullingService Instance = new EmptyPluginRenderCullingService();
    private EmptyPluginRenderCullingService() { }
    public IPluginRegistration RegisterPolicy(PluginRenderCullingPolicy policy) => throw new NotSupportedException("Render-culling policies are unavailable in this host.");
}

internal sealed class EmptyPluginNpcTargetSnapshotService : IPluginNpcTargetSnapshotService
{
    internal static readonly EmptyPluginNpcTargetSnapshotService Instance = new EmptyPluginNpcTargetSnapshotService();
    private EmptyPluginNpcTargetSnapshotService() { }
    public void CopyHostileNpcTargets(System.Collections.Generic.ICollection<PluginNpcTargetSnapshot> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
    }
}

internal sealed class EmptyPluginWorldSectionService : IPluginWorldSectionService
{
    internal static readonly EmptyPluginWorldSectionService Instance = new EmptyPluginWorldSectionService();
    private EmptyPluginWorldSectionService() { }
    public void CopyVisibleSections(System.Collections.Generic.ICollection<PluginWorldSectionSnapshot> destination, int margin = 0)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));
    }
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
