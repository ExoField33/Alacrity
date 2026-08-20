using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Action-button, interactive presentation, and outgoing-transform portions of the
/// chat host. All snapshots are rebuilt only when registrations change; Terraria reads them
/// without taking the chat registry lock.</summary>
public sealed partial class PluginChatHost
{
    private const int MaximumRetainedPresentations = 512;
    // An outgoing transformer owns the native chat submit only briefly.  It must never be able
    // to leave the textbox permanently deferred when a remote service or plugin callback hangs.
    internal static TimeSpan OutgoingTransformTimeout { get; set; } = TimeSpan.FromSeconds(10);
    private readonly List<MessageActionEntry> messageActions = new List<MessageActionEntry>();
    private readonly List<ChatActionButtonEntry> actionButtons = new List<ChatActionButtonEntry>();
    private readonly List<OutgoingTransformerEntry> outgoingTransformers = new List<OutgoingTransformerEntry>();
    /// <summary>Fast-path state used by Terraria before drawing the chat action strip.</summary>
    public bool HasActionButtons => Volatile.Read(ref actionButtonSnapshot).Entries.Length != 0;

    /// <summary>Fast-path state used before routing interactive chat spans.</summary>
    public bool HasMessageActions => Volatile.Read(ref messageActionSnapshot).Length != 0;

    /// <summary>Returns an immutable, priority-ordered action-strip view without a registry lock.</summary>
    public IReadOnlyList<ChatActionButtonView> GetActionButtons()
    {
        return Volatile.Read(ref actionButtonSnapshot).Views;
    }

    /// <summary>Resolves one action-button border state with callback failure isolation.</summary>
    public bool TryGetActionButtonVisualState(PluginId owner, string id, out ChatActionButtonVisualState state)
    {
        ChatActionButtonEntry? entry = FindActionButton(owner, id);
        if (entry == null || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            state = default;
            return false;
        }

        try
        {
            using (lease)
            {
                state = entry.Handler.GetVisualState();
                return true;
            }
        }
        catch (Exception exception)
        {
            ReportFailure(entry, "chat action button visual state", exception);
            entry.Dispose();
            state = default;
            return false;
        }
    }

    /// <summary>Activates a button click after the integration has performed pointer hit testing.</summary>
    public bool TryActivateActionButton(PluginId owner, string id, bool shift)
    {
        return TryActivateActionButton(owner, id, ChatActionButtonMouseButton.Left, shift);
    }

    /// <summary>Activates a button click after the integration has performed pointer hit testing.</summary>
    public bool TryActivateActionButton(PluginId owner, string id, ChatActionButtonMouseButton button, bool shift)
    {
        ChatActionButtonEntry? entry = FindActionButton(owner, id);
        if (entry == null || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            return false;
        }

        try
        {
            using (lease)
            {
                entry.Handler.Activate(new ChatActionButtonInvocation(button, shift));
            }

            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(entry, "chat action button", exception);
            entry.Dispose();
            return false;
        }
    }

    /// <summary>Builds menu rows only while a host-owned action popover is visible.</summary>
    public IReadOnlyList<ChatActionMenuItem> GetActionButtonMenuItems(PluginId owner, string id)
    {
        ChatActionButtonEntry? entry = FindActionButton(owner, id);
        if (entry == null || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            return Array.Empty<ChatActionMenuItem>();
        }

        try
        {
            using (lease)
            {
                return entry.Handler.GetMenuItems() ?? Array.Empty<ChatActionMenuItem>();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(entry, "chat action menu", exception);
            entry.Dispose();
            return Array.Empty<ChatActionMenuItem>();
        }
    }

    /// <summary>Activates one visible menu row after checking current row availability.</summary>
    public bool TryActivateActionButtonMenuItem(PluginId owner, string buttonId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        ChatActionButtonEntry? entry = FindActionButton(owner, buttonId);
        if (entry == null || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            return false;
        }

        try
        {
            using (lease)
            {
                IReadOnlyList<ChatActionMenuItem> items = entry.Handler.GetMenuItems() ?? Array.Empty<ChatActionMenuItem>();
                if (TryFindActionMenuItem(items, itemId, out ChatActionMenuItem? item) && item != null && item.Enabled && !item.HasChildren)
                {
                    entry.Handler.ActivateMenuItem(itemId);
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            ReportFailure(entry, "chat action menu item", exception);
            entry.Dispose();
        }

        return false;
    }

    // Nested menu rows are host navigation only. Only a discovered leaf can enter plugin code.
    private static bool TryFindActionMenuItem(IReadOnlyList<ChatActionMenuItem> items, string id, out ChatActionMenuItem? result)
    {
        for (int index = 0; index < items.Count; index++)
        {
            ChatActionMenuItem item = items[index];
            if (item == null)
            {
                continue;
            }

            if (string.Equals(item.Id, id, StringComparison.Ordinal))
            {
                result = item;
                return true;
            }

            if (item.HasChildren && TryFindActionMenuItem(item.Children, id, out result))
            {
                return true;
            }
        }

        result = null;
        return false;
    }

    /// <summary>Dispatches an interactive text span to its registered owner.</summary>
    public bool TryActivateMessageAction(PluginId owner, string id, ChatMessageHandle message, string target, bool shift)
    {
        MessageActionEntry[] current = Volatile.Read(ref messageActionSnapshot);
        for (int index = 0; index < current.Length; index++)
        {
            MessageActionEntry entry = current[index];
            if (entry.Owner != owner || !string.Equals(entry.Descriptor.Id, id, StringComparison.Ordinal) || !entry.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                continue;
            }

            try
            {
                using (lease)
                {
                    return entry.Handler.TryActivate(new ChatMessageActionInvocation(message, target, shift));
                }
            }
            catch (Exception exception)
            {
                ReportFailure(entry, "chat message action", exception);
                entry.Dispose();
                return false;
            }
        }

        return false;
    }

    /// <summary>Stores an owner-validated same-shape replacement presentation for a rendered chat segment.</summary>
    private bool TryUpdateMessagePresentation(PluginId owner, ChatMessageHandle message, ChatMessagePresentation presentation)
    {
        if (!message.IsValid || presentation == null)
        {
            return false;
        }

        Action<ChatMessageHandle>? updated;
        lock (gate)
        {
            if (!presentations.TryGetValue(message.Value, out MessagePresentationEntry entry) || entry.Owner != owner || entry.SpanCount != presentation.Spans.Count)
            {
                return false;
            }

            entry.Presentation = presentation;
            entry.Version++;
            updated = MessagePresentationUpdated;
        }

        // Rendering adapters only enqueue a native refresh here. Do not let an optional
        // integration observer turn a successful scoped presentation update into a failure.
        try
        {
            updated?.Invoke(message);
        }
        catch
        {
        }

        return true;
    }

    /// <summary>Reads the latest presentation without exposing mutable Terraria snippets to plugins.</summary>
    public bool TryGetMessagePresentation(ChatMessageHandle message, out ChatMessagePresentation? presentation, out int version)
    {
        if (!message.IsValid)
        {
            presentation = null;
            version = 0;
            return false;
        }

        lock (gate)
        {
            if (presentations.TryGetValue(message.Value, out MessagePresentationEntry entry) && entry.Presentation != null)
            {
                presentation = entry.Presentation;
                version = entry.Version;
                return true;
            }
        }

        presentation = null;
        version = 0;
        return false;
    }

    /// <summary>Starts one asynchronous transform if no activation currently owns the outgoing input.</summary>
    public bool TryDeferOutgoingMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        lock (gate)
        {
            if (readyOutgoingSubmission != null && string.Equals(readyOutgoingSubmission, text, StringComparison.Ordinal))
            {
                readyOutgoingSubmission = null;
                readyOutgoingSource = null;
                readyOutgoingOwner = default;
                return false;
            }

            if (pendingOutgoing != null)
            {
                return true;
            }
        }

        ChatOutgoingMessageSnapshot message = new ChatOutgoingMessageSnapshot(text);
        if (message.IsCommand)
        {
            return false;
        }

        OutgoingTransformerEntry[] current = Volatile.Read(ref outgoingTransformerSnapshot);
        for (int index = 0; index < current.Length; index++)
        {
            OutgoingTransformerEntry entry = current[index];
            if (!entry.TryEnter(out ActivationCallbackGate.Lease lease))
            {
                continue;
            }

            bool accepts;
            try
            {
                using (lease)
                {
                    accepts = entry.Transformer.CanTransform(message);
                }
            }
            catch (Exception exception)
            {
                ReportFailure(entry, "outgoing chat transformer admission", exception);
                entry.Dispose();
                continue;
            }

            if (!accepts)
            {
                continue;
            }

            var pending = new PendingOutgoingMessage(entry, message);
            lock (gate)
            {
                if (pendingOutgoing != null)
                {
                    return true;
                }

                if (entry.IsReleased || !entry.IsAdmissionOpen)
                {
                    return false;
                }

                pendingOutgoing = pending;
            }

            try
            {
                entry.Scheduler.RunBackground("chat-outgoing:" + entry.Descriptor.Id, cancellationToken => TransformOutgoingAsync(pending, cancellationToken));
                return true;
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    if (ReferenceEquals(pendingOutgoing, pending))
                    {
                        pendingOutgoing = null;
                    }
                }
                entry.Logger.Error("Outgoing chat transform could not start for plugin '" + entry.Owner.Value + "'.", exception);
                return false;
            }
        }

        return false;
    }

    /// <summary>Consumes a completed outgoing transformation. Failures deliberately leave the original input unsent.</summary>
    public bool TryTakeReadyOutgoingMessage(out string replacement)
    {
        lock (gate)
        {
            if (pendingOutgoing == null || !pendingOutgoing.Completed)
            {
                replacement = string.Empty;
                return false;
            }

            PendingOutgoingMessage pending = pendingOutgoing;
            pendingOutgoing = null;
            if (!pending.Result.Success || string.IsNullOrEmpty(pending.Result.Text))
            {
                if (!string.IsNullOrWhiteSpace(pending.Result.Diagnostic))
                {
                    pending.Entry.Logger.Error("Outgoing chat transform failed for plugin '" + pending.Entry.Owner.Value + "': " + pending.Result.Diagnostic, null);
                }
                replacement = string.Empty;
                return false;
            }

            replacement = pending.Result.Text;
            readyOutgoingSubmission = replacement;
            readyOutgoingSource = pending.Message.Text;
            readyOutgoingOwner = pending.Entry.Owner;
            return true;
        }
    }

    /// <summary>
    /// Observes the current native player-chat text.  Editing the field while a transform is
    /// pending revokes that transform's ownership, so a stale completion cannot submit or
    /// replace a newer line.
    /// </summary>
    public void ObserveOutgoingInput(string text)
    {
        PendingOutgoingMessage? cancelled = null;
        text = text ?? string.Empty;
        lock (gate)
        {
            if (pendingOutgoing != null && !string.Equals(pendingOutgoing.Message.Text, text, StringComparison.Ordinal))
            {
                cancelled = pendingOutgoing;
                pendingOutgoing = null;
            }

            if (readyOutgoingSubmission != null &&
                !string.Equals(readyOutgoingSource, text, StringComparison.Ordinal) &&
                !string.Equals(readyOutgoingSubmission, text, StringComparison.Ordinal))
            {
                readyOutgoingSubmission = null;
                readyOutgoingSource = null;
                readyOutgoingOwner = default;
            }
        }

        cancelled?.Cancel();
    }

    private async Task TransformOutgoingAsync(PendingOutgoingMessage pending, CancellationToken cancellationToken)
    {
        ChatOutgoingMessageTransformResult result;
        if (!pending.Entry.TryEnter(out ActivationCallbackGate.Lease lease))
        {
            return;
        }

        try
        {
            using (lease)
            {
                var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pending.Cancellation.Token);
                try
                {
                    Task<ChatOutgoingMessageTransformResult> transformTask = pending.Entry.Transformer.TransformAsync(pending.Message, linkedCancellation.Token);
                    Task timeoutTask = Task.Delay(OutgoingTransformTimeout, linkedCancellation.Token);
                    if (await Task.WhenAny(transformTask, timeoutTask).ConfigureAwait(false) != transformTask)
                    {
                        linkedCancellation.Cancel();
                        ObserveTimedOutTransform(transformTask, linkedCancellation);
                        ClearPendingFailure(pending, "The outgoing chat transform timed out.");
                        return;
                    }

                    result = await transformTask.ConfigureAwait(false) ?? ChatOutgoingMessageTransformResult.Fail();
                }
                catch
                {
                    linkedCancellation.Dispose();
                    throw;
                }

                linkedCancellation.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || pending.Cancellation.IsCancellationRequested || pending.Entry.IsReleased)
        {
            ClearPendingFailure(pending, null);
            return;
        }
        catch (Exception exception)
        {
            pending.Entry.Logger.Error("Outgoing chat transformer '" + pending.Entry.Descriptor.Id + "' failed for plugin '" + pending.Entry.Owner.Value + "'.", exception);
            ClearPendingFailure(pending, "Translation request failed.");
            return;
        }

        lock (gate)
        {
            if (ReferenceEquals(pendingOutgoing, pending) && pending.Entry.IsAdmissionOpen)
            {
                pending.Result = result;
                pending.Completed = true;
            }
        }
    }

    private void ClearPendingFailure(PendingOutgoingMessage pending, string? diagnostic)
    {
        bool report = false;
        lock (gate)
        {
            if (ReferenceEquals(pendingOutgoing, pending))
            {
                pendingOutgoing = null;
                report = !string.IsNullOrWhiteSpace(diagnostic);
            }
        }

        if (report)
        {
            pending.Entry.Logger.Error("Outgoing chat transform failed for plugin '" + pending.Entry.Owner.Value + "': " + diagnostic, null);
        }
    }

    private static void ObserveTimedOutTransform(Task<ChatOutgoingMessageTransformResult> transformTask, CancellationTokenSource cancellation)
    {
        // A plugin may temporarily ignore cancellation.  Preserve its token source until it
        // finishes, observe any eventual exception, and avoid keeping the chat submission owned.
        _ = transformTask.ContinueWith(
            task =>
            {
                _ = task.Exception;
                cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private ChatActionButtonEntry? FindActionButton(PluginId owner, string id)
    {
        ChatActionButtonEntry[] current = Volatile.Read(ref actionButtonSnapshot).Entries;
        for (int index = 0; index < current.Length; index++)
        {
            if (current[index].Owner == owner && string.Equals(current[index].Descriptor.Id, id, StringComparison.Ordinal))
            {
                return current[index];
            }
        }

        return null;
    }

    private void TrimPresentations()
    {
        if (presentations.Count <= MaximumRetainedPresentations)
        {
            return;
        }

        long oldest = long.MaxValue;
        foreach (long handle in presentations.Keys)
        {
            if (handle < oldest)
            {
                oldest = handle;
            }
        }

        presentations.Remove(oldest);
    }

    private void RemovePresentations(PluginId owner)
    {
        var removed = new List<long>();
        foreach (KeyValuePair<long, MessagePresentationEntry> pair in presentations)
        {
            if (pair.Value.Owner == owner)
            {
                removed.Add(pair.Key);
            }
        }

        for (int index = 0; index < removed.Count; index++)
        {
            presentations.Remove(removed[index]);
        }
    }

    private sealed class MessagePresentationEntry
    {
        internal MessagePresentationEntry(PluginId owner, int spanCount)
        {
            Owner = owner;
            SpanCount = spanCount;
        }

        internal PluginId Owner { get; }
        internal int SpanCount { get; }
        internal ChatMessagePresentation? Presentation { get; set; }
        internal int Version { get; set; }
    }

    private sealed class PendingOutgoingMessage
    {
        internal PendingOutgoingMessage(OutgoingTransformerEntry entry, ChatOutgoingMessageSnapshot message)
        {
            Entry = entry;
            Message = message;
            Result = ChatOutgoingMessageTransformResult.Fail();
            Cancellation = new CancellationTokenSource();
        }

        internal OutgoingTransformerEntry Entry { get; }
        internal ChatOutgoingMessageSnapshot Message { get; }
        internal ChatOutgoingMessageTransformResult Result { get; set; }
        internal bool Completed { get; set; }
        internal CancellationTokenSource Cancellation { get; }

        internal void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Cancellation is intentionally idempotent across teardown and stale input.
            }
        }
    }

    private sealed class MessageActionEntry : Entry
    {
        internal MessageActionEntry(PluginId owner, ChatMessageActionDescriptor descriptor, IChatMessageActionHandler handler, Action<MessageActionEntry> remove, ActivationCallbackGate? callbackGate)
            : base(owner, "chat-message-action:" + descriptor.Id, entry => remove((MessageActionEntry)entry), callbackGate)
        {
            Descriptor = descriptor;
            Handler = handler;
        }

        internal ChatMessageActionDescriptor Descriptor { get; }
        internal IChatMessageActionHandler Handler { get; }
    }

    private sealed class ChatActionButtonEntry : Entry
    {
        internal ChatActionButtonEntry(PluginId owner, ChatActionButtonDescriptor descriptor, IChatActionButtonHandler handler, Action<ChatActionButtonEntry> remove, ActivationCallbackGate? callbackGate)
            : base(owner, "chat-action-button:" + descriptor.Id, entry => remove((ChatActionButtonEntry)entry), callbackGate)
        {
            Descriptor = descriptor;
            Handler = handler;
        }

        internal ChatActionButtonDescriptor Descriptor { get; }
        internal IChatActionButtonHandler Handler { get; }
    }

    private sealed class OutgoingTransformerEntry : Entry
    {
        internal OutgoingTransformerEntry(PluginId owner, ChatOutgoingMessageTransformerDescriptor descriptor, IChatOutgoingMessageTransformer transformer, IPluginScheduler scheduler, IPluginLogger logger, Action<OutgoingTransformerEntry> remove, ActivationCallbackGate? callbackGate)
            : base(owner, "chat-outgoing-transformer:" + descriptor.Id, entry => remove((OutgoingTransformerEntry)entry), callbackGate)
        {
            Descriptor = descriptor;
            Transformer = transformer;
            Scheduler = scheduler;
            Logger = logger;
        }

        internal ChatOutgoingMessageTransformerDescriptor Descriptor { get; }
        internal IChatOutgoingMessageTransformer Transformer { get; }
        internal IPluginScheduler Scheduler { get; }
        internal IPluginLogger Logger { get; }
    }
}
