using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Fast-path state used by Terraria integration before entering a chat hook.</summary>
    public bool HasInputEditors { get { lock (gate) return editors.Count != 0; } }

    /// <summary>Fast-path state used by Terraria integration before parsing presentation spans.</summary>
    public bool HasMessageDecorators { get { lock (gate) return decorators.Count != 0; } }

    /// <summary>Fast-path state used before the host displays a classified chat message.</summary>
    public bool HasMessageFilters { get { lock (gate) return filters.Count != 0; } }

    /// <summary>Fast-path state used by Terraria integration before attempting external link activation.</summary>
    public bool HasLinkHandlers { get { lock (gate) return links.Count != 0; } }

    /// <summary>Creates a plugin-owned chat service set after manifest validation.</summary>
    public IPluginChatService CreateService(PluginManifest manifest, IPluginResourceScope resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (!manifest.Id.IsValid) throw new ArgumentException("Chat services require a valid plugin owner.", nameof(manifest));
        return new ScopedService(this, manifest.Id, resources);
    }

    public ChatInputEditResult Edit(ChatInputSnapshot snapshot, ChatInputAction action)
    {
        EditorEntry[] current;
        lock (gate) current = editors.OrderBy(entry => entry.Descriptor.Priority).ToArray();
        foreach (var entry in current)
        {
            try
            {
                var result = entry.Editor.Edit(snapshot, action) ?? ChatInputEditResult.Unhandled(snapshot);
                if (result.Handled) return result;
            }
            catch { entry.Dispose(); }
        }
        return ChatInputEditResult.Unhandled(snapshot);
    }

    public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
    {
        DecoratorEntry[] current;
        lock (gate) current = decorators.OrderBy(entry => entry.Descriptor.Priority).ToArray();
        IReadOnlyList<ChatTextSpan> result = new[] { new ChatTextSpan(message.Text) };
        foreach (var entry in current)
        {
            try { result = entry.Decorator.Decorate(message) ?? result; }
            catch { entry.Dispose(); }
        }
        return result;
    }

    public bool ShouldDisplay(ChatMessageOrigin origin)
    {
        FilterEntry[] current;
        lock (gate) current = filters.OrderBy(entry => entry.Descriptor.Priority).ToArray();
        foreach (var entry in current)
        {
            try
            {
                if (!entry.Filter.ShouldDisplay(origin)) return false;
            }
            catch { entry.Dispose(); }
        }
        return true;
    }

    public bool TryActivate(Uri uri)
    {
        if (uri == null) throw new ArgumentNullException(nameof(uri));
        LinkEntry entry;
        lock (gate) { if (!links.TryGetValue(uri.Scheme, out entry)) return false; }
        try { return entry.Handler.TryActivate(uri); }
        catch { entry.Dispose(); return false; }
    }

    private IPluginRegistration RegisterEditor(PluginId owner, IPluginResourceScope resources, ChatInputEditorDescriptor descriptor, IChatInputEditor editor)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (editor == null) throw new ArgumentNullException(nameof(editor));
        var entry = new EditorEntry(owner, descriptor, editor, RemoveEditor);
        lock (gate) editors.Add(entry);
        return Own(resources, entry, "chat-editor:" + descriptor.Id);
    }
    private IPluginRegistration RegisterDecorator(PluginId owner, IPluginResourceScope resources, ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (decorator == null) throw new ArgumentNullException(nameof(decorator));
        var entry = new DecoratorEntry(owner, descriptor, decorator, RemoveDecorator);
        lock (gate) decorators.Add(entry);
        return Own(resources, entry, "chat-decorator:" + descriptor.Id);
    }
    private IPluginRegistration RegisterFilter(PluginId owner, IPluginResourceScope resources, ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (filter == null) throw new ArgumentNullException(nameof(filter));
        var entry = new FilterEntry(owner, descriptor, filter, RemoveFilter);
        lock (gate) filters.Add(entry);
        return Own(resources, entry, "chat-filter:" + descriptor.Id);
    }
    private IPluginRegistration RegisterLink(PluginId owner, IPluginResourceScope resources, ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler)
    {
        if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var entry = new LinkEntry(owner, descriptor.Scheme, handler, RemoveLink);
        lock (gate)
        {
            if (links.ContainsKey(descriptor.Scheme)) throw new InvalidOperationException("A chat link handler is already registered for " + descriptor.Scheme + ".");
            links.Add(descriptor.Scheme, entry);
        }
        return Own(resources, entry, "chat-link:" + descriptor.Scheme);
    }
    private static IPluginRegistration Own(IPluginResourceScope scope, IPluginRegistration registration, string name) { scope.Own(name, PluginResourceKind.UserInterface, registration); return registration; }
    private void RemoveEditor(EditorEntry entry) { lock (gate) editors.Remove(entry); }
    private void RemoveDecorator(DecoratorEntry entry) { lock (gate) decorators.Remove(entry); }
    private void RemoveFilter(FilterEntry entry) { lock (gate) filters.Remove(entry); }
    private void RemoveLink(LinkEntry entry) { lock (gate) if (links.TryGetValue(entry.Scheme, out var current) && ReferenceEquals(current, entry)) links.Remove(entry.Scheme); }

    private sealed class ScopedService : IPluginChatService
    {
        private readonly PluginChatHost host; private readonly PluginId owner; private readonly IPluginResourceScope resources;
        public ScopedService(PluginChatHost host, PluginId owner, IPluginResourceScope resources) { this.host = host; this.owner = owner; this.resources = resources; }
        public IPluginRegistration RegisterInputEditor(ChatInputEditorDescriptor descriptor, IChatInputEditor editor) => host.RegisterEditor(owner, resources, descriptor, editor);
        public IPluginRegistration RegisterMessageDecorator(ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator) => host.RegisterDecorator(owner, resources, descriptor, decorator);
        public IPluginRegistration RegisterMessageFilter(ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter) => host.RegisterFilter(owner, resources, descriptor, filter);
        public IPluginRegistration RegisterLinkHandler(ChatLinkHandlerDescriptor descriptor, IChatLinkHandler handler) => host.RegisterLink(owner, resources, descriptor, handler);
    }
    private abstract class Entry : IPluginRegistration { private readonly Action<Entry> remove; private bool released; protected Entry(PluginId owner, string name, Action<Entry> remove) { Owner = owner; Name = name; this.remove = remove; } public PluginId Owner { get; } public string Name { get; } public bool IsReleased => released; public void Dispose() { if (released) return; released = true; remove(this); } }
    private sealed class EditorEntry : Entry { public EditorEntry(PluginId owner, ChatInputEditorDescriptor descriptor, IChatInputEditor editor, Action<EditorEntry> remove) : base(owner, "chat-editor:" + descriptor.Id, entry => remove((EditorEntry)entry)) { Descriptor = descriptor; Editor = editor; } public ChatInputEditorDescriptor Descriptor { get; } public IChatInputEditor Editor { get; } }
    private sealed class DecoratorEntry : Entry { public DecoratorEntry(PluginId owner, ChatMessageDecoratorDescriptor descriptor, IChatMessageDecorator decorator, Action<DecoratorEntry> remove) : base(owner, "chat-decorator:" + descriptor.Id, entry => remove((DecoratorEntry)entry)) { Descriptor = descriptor; Decorator = decorator; } public ChatMessageDecoratorDescriptor Descriptor { get; } public IChatMessageDecorator Decorator { get; } }
    private sealed class FilterEntry : Entry { public FilterEntry(PluginId owner, ChatMessageFilterDescriptor descriptor, IChatMessageFilter filter, Action<FilterEntry> remove) : base(owner, "chat-filter:" + descriptor.Id, entry => remove((FilterEntry)entry)) { Descriptor = descriptor; Filter = filter; } public ChatMessageFilterDescriptor Descriptor { get; } public IChatMessageFilter Filter { get; } }
    private sealed class LinkEntry : Entry { public LinkEntry(PluginId owner, string scheme, IChatLinkHandler handler, Action<LinkEntry> remove) : base(owner, "chat-link:" + scheme, entry => remove((LinkEntry)entry)) { Scheme = scheme; Handler = handler; } public string Scheme { get; } public IChatLinkHandler Handler { get; } }
}

/// <summary>Concrete Terraria service grouping assembled by the host.</summary>
public sealed class PluginTerrariaServices : ITerrariaServices
{
    public PluginTerrariaServices(IPluginChatService chat) { Chat = chat ?? throw new ArgumentNullException(nameof(chat)); }
    public IPluginChatService Chat { get; }
}
