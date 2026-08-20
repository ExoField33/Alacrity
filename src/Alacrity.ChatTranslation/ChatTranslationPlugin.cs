using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.ChatTranslation;

/// <summary>Session-local Google Translate chat presentation. All network work uses the
/// generic host transport and all visible message changes use host-owned chat presentations.</summary>
public sealed class ChatTranslationPlugin : IAlacrityPlugin
{
    private const string TranslationActionId = "toggle-translation";
    private const string ActionButtonId = "translation";
    private const string SourceLanguagePrefix = "source-language:";
    private const string TargetLanguagePrefix = "target-language:";
    private const string OutgoingLanguagePrefix = "outgoing-language:";
    private const int AutomaticIncomingTranslationDelayMilliseconds = 150;
    private static readonly PluginColor IncomingBackground = new PluginColor(98, 186, 102);
    private static readonly PluginColor OutgoingBackground = new PluginColor(205, 78, 78);
    private static readonly PluginColor AttributionColor = new PluginColor(150, 150, 150);
    private readonly object gate = new object();
    private readonly Dictionary<long, MessageState> messages = new Dictionary<long, MessageState>();
    private readonly Queue<MessageState> automaticIncomingQueue = new Queue<MessageState>();
    private ChatActionMenuItem[] menuItems = Array.Empty<ChatActionMenuItem>();
    private ChatActionMenuItem[] sourceLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
    private ChatActionMenuItem[] targetLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
    private ChatActionMenuItem[] outgoingLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
    private IPluginContext? context;
    private IPluginSetting<bool>? autoIncomingSetting;
    private IPluginSetting<bool>? autoOutgoingSetting;
    private IPluginSetting<bool>? showTranslationAttributionSetting;
    private IPluginSetting<string>? sourceLanguageSetting;
    private IPluginSetting<string>? targetLanguageSetting;
    private IPluginSetting<string>? outgoingLanguageSetting;
    private GoogleTranslationClient? translator;
    private bool autoIncoming;
    private bool autoOutgoing;
    private int showTranslationAttribution = 1;
    private string sourceLanguage = "auto";
    private string targetLanguage = "en";
    private string outgoingLanguage = "en";
    private int active;
    private int activationGeneration;
    private bool automaticIncomingWorkerActive;
    private int automaticIncomingWorkerGeneration;

    /// <inheritdoc />
    public void Initialize(IPluginContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        Volatile.Write(ref active, 0);
        Interlocked.Increment(ref activationGeneration);
        translator = new GoogleTranslationClient(context.Network, context.Logger);
        autoIncomingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("autoIncoming", false));
        autoOutgoingSetting = context.Settings.Register(new PluginSettingDefinition<bool>("autoOutgoing", false));
        showTranslationAttributionSetting = context.Settings.Register(new PluginSettingDefinition<bool>("showTranslationAttribution", true));
        sourceLanguageSetting = context.Settings.Register(new PluginSettingDefinition<string>("sourceLanguage", "auto", NormalizeSourceLanguage));
        targetLanguageSetting = context.Settings.Register(new PluginSettingDefinition<string>("targetLanguage", "en", NormalizeTargetLanguage));
        outgoingLanguageSetting = context.Settings.Register(new PluginSettingDefinition<string>("outgoingLanguage", "en", NormalizeTargetLanguage));
        sourceLanguageMenuItems = CreateLanguageMenuItems(SourceLanguagePrefix, TranslationLanguageCatalog.DefaultSources);
        targetLanguageMenuItems = CreateLanguageMenuItems(TargetLanguagePrefix, TranslationLanguageCatalog.DefaultTargets);
        outgoingLanguageMenuItems = CreateLanguageMenuItems(OutgoingLanguagePrefix, TranslationLanguageCatalog.DefaultTargets);

        lock (gate)
        {
            autoIncoming = autoIncomingSetting.Value;
            autoOutgoing = autoOutgoingSetting.Value;
            Volatile.Write(ref showTranslationAttribution, showTranslationAttributionSetting.Value ? 1 : 0);
            sourceLanguage = sourceLanguageSetting.Value;
            targetLanguage = targetLanguageSetting.Value;
            outgoingLanguage = outgoingLanguageSetting.Value;
        }

        autoIncomingSetting.Subscribe(value => SetAutoIncoming(value));
        autoOutgoingSetting.Subscribe(value => SetAutoOutgoing(value));
        showTranslationAttributionSetting.Subscribe(value => SetShowTranslationAttribution(value));
        sourceLanguageSetting.Subscribe(value => SetSourceLanguage(value));
        targetLanguageSetting.Subscribe(value => SetTargetLanguage(value));
        outgoingLanguageSetting.Subscribe(value => SetOutgoingLanguage(value));
        RefreshMenuItems();

        context.Ui.RegisterSettingsPage(new PluginUiContribution("chat-translation", "Chat Translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("auto-incoming", "Auto Translate Incoming", autoIncomingSetting).InPage("chat-translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Dropdown("source-language", "Incoming Translate From", GetSourceLanguageOptions, sourceLanguageSetting).InPage("chat-translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Dropdown("target-language", "Translate To", GetTargetLanguageOptions, targetLanguageSetting).InPage("chat-translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("show-translation-attribution", "Show Translated From", showTranslationAttributionSetting).InPage("chat-translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Toggle("auto-outgoing", "Translate Outgoing", autoOutgoingSetting).InPage("chat-translation"));
        context.Ui.RegisterSettingsControl(PluginSettingControl.Dropdown("outgoing-language", "Outgoing Language", GetTargetLanguageOptions, outgoingLanguageSetting).InPage("chat-translation"));

        context.Terraria.Chat.RegisterMessageDecorator(new ChatMessageDecoratorDescriptor("chat-translation", priority: 100), new IncomingDecorator(this));
        context.Terraria.Chat.RegisterMessageAction(new ChatMessageActionDescriptor(TranslationActionId), new MessageAction(this));
        context.Terraria.Chat.RegisterActionButton(
            new ChatActionButtonDescriptor(ActionButtonId, "assets/translate-icon", priority: 100, new PluginTooltipOptions("Chat Translation", PluginTooltipPlacement.Above)),
            new ActionButton(this));
        context.Terraria.Chat.RegisterOutgoingMessageTransformer(new ChatOutgoingMessageTransformerDescriptor("chat-translation", priority: 100), new OutgoingTransformer(this));
        context.Events.Subscribe<ClientMenuStateChangedEvent>(OnMenuStateChanged);
    }

    /// <inheritdoc />
    public void Enable()
    {
        Volatile.Write(ref active, 1);
    }

    /// <inheritdoc />
    public void Disable()
    {
        Volatile.Write(ref active, 0);
        Interlocked.Increment(ref activationGeneration);
        ClearSession();
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        Volatile.Write(ref active, 0);
        Interlocked.Increment(ref activationGeneration);
        ClearSession();
        autoIncomingSetting = null;
        autoOutgoingSetting = null;
        showTranslationAttributionSetting = null;
        sourceLanguageSetting = null;
        targetLanguageSetting = null;
        outgoingLanguageSetting = null;
        translator = null;
        context = null;
        sourceLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
        targetLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
        outgoingLanguageMenuItems = Array.Empty<ChatActionMenuItem>();
    }

    private void OnMenuStateChanged(ClientMenuStateChangedEvent change)
    {
        if (change.IsGameMenu)
        {
            ClearSession();
        }
    }

    private IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message, IReadOnlyList<ChatTextSpan> currentSpans)
    {
        if (!IsActive || !message.Handle.IsValid || currentSpans.Count == 0 || ContainsLink(currentSpans))
        {
            return currentSpans;
        }

        string text = Concatenate(currentSpans);
        if (!CanTranslateText(text))
        {
            return currentSpans;
        }

        MessageState state;
        GoogleTranslationClient.TranslationResult? translation;
        bool translateAutomatically;
        lock (gate)
        {
            if (!messages.TryGetValue(message.Handle.Value, out state))
            {
                state = new MessageState(message.Handle, text);
                messages.Add(message.Handle.Value, state);
                TrimMessages();
            }

            translation = state.Translation;
            translateAutomatically = autoIncoming;
        }

        if (translateAutomatically)
        {
            QueueAutomaticIncomingTranslation(state);
        }

        return BuildPresentation(state.OriginalText, translation);
    }

    private bool ToggleMessage(ChatMessageActionInvocation invocation)
    {
        if (!IsActive || !invocation.Message.IsValid)
        {
            return false;
        }

        MessageState? state;
        lock (gate)
        {
            messages.TryGetValue(invocation.Message.Value, out state);
        }

        if (state == null)
        {
            return false;
        }

        bool restoreOriginal;
        lock (gate)
        {
            if (state.IsTranslated)
            {
                state.IsTranslated = false;
                state.Translation = null;
                restoreOriginal = true;
            }
            else
            {
                restoreOriginal = false;
            }
        }

        if (restoreOriginal)
        {
            UpdatePresentation(state.Handle, state.OriginalText, null, Volatile.Read(ref activationGeneration));
            return true;
        }

        StartIncomingTranslation(state);
        return true;
    }

    private void StartIncomingTranslation(MessageState state)
    {
        IPluginContext? current = context;
        int generation = Volatile.Read(ref activationGeneration);
        string source;
        string target;
        GoogleTranslationClient? currentTranslator = translator;
        lock (gate)
        {
            if (!IsActive || current == null || state.IsTranslated || state.IsPending || !messages.TryGetValue(state.Handle.Value, out MessageState currentState) || !ReferenceEquals(currentState, state))
            {
                return;
            }

            source = sourceLanguage;
            target = targetLanguage;
            if (!CanPossiblyMatchIncomingSource(state.OriginalText, source))
            {
                return;
            }

            if (currentTranslator != null)
            {
                state.IsPending = true;
            }
        }

        if (currentTranslator == null)
        {
            return;
        }

        try
        {
            current.Scheduler.RunBackground("translate-incoming-chat", async cancellationToken =>
            {
                GoogleTranslationClient.TranslationResult? result = await currentTranslator!.TranslateAsync(state.OriginalText, source, target, cancellationToken).ConfigureAwait(false);
                bool shouldUpdate = false;
                lock (gate)
                {
                    if (!IsCurrentGeneration(generation) || !messages.TryGetValue(state.Handle.Value, out MessageState currentState) || !ReferenceEquals(currentState, state))
                    {
                        return;
                    }

                    state.IsPending = false;
                    if (result != null && !SameLanguage(result.SourceLanguage, target) && !string.Equals(result.Text, state.OriginalText, StringComparison.Ordinal))
                    {
                        state.Translation = result;
                        state.IsTranslated = true;
                        shouldUpdate = true;
                    }
                }

                if (shouldUpdate && result != null)
                {
                    UpdatePresentation(state.Handle, state.OriginalText, result, generation);
                }
            });
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                state.IsPending = false;
            }
            current.Logger.Error("Chat translation could not start incoming work.", exception);
        }
    }

    // Incoming message arrival can be bursty. Keep the public translation endpoint and the
    // activation's background quota healthy by translating one retained line at a time.
    private void QueueAutomaticIncomingTranslation(MessageState state)
    {
        IPluginContext? current = context;
        int generation = Volatile.Read(ref activationGeneration);
        bool startWorker = false;
        lock (gate)
        {
            if (!IsCurrentGeneration(generation) || current == null || !autoIncoming ||
                state.IsTranslated || state.IsPending || state.AutomaticAttempted ||
                !messages.TryGetValue(state.Handle.Value, out MessageState currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            if (!CanPossiblyMatchIncomingSource(state.OriginalText, sourceLanguage))
            {
                state.AutomaticAttempted = true;
                return;
            }

            state.IsPending = true;
            state.AutomaticAttempted = true;
            automaticIncomingQueue.Enqueue(state);
            if (!automaticIncomingWorkerActive || automaticIncomingWorkerGeneration != generation)
            {
                automaticIncomingWorkerActive = true;
                automaticIncomingWorkerGeneration = generation;
                startWorker = true;
            }
        }

        if (!startWorker)
        {
            return;
        }

        try
        {
            current.Scheduler.RunBackground(
                "translate-incoming-chat-queue",
                cancellationToken => RunAutomaticIncomingTranslationsAsync(generation, cancellationToken));
        }
        catch (Exception exception)
        {
            AbortAutomaticIncomingQueue(generation);
            current.Logger.Error("Chat translation could not start automatic incoming work.", exception);
        }
    }

    private async Task RunAutomaticIncomingTranslationsAsync(int generation, CancellationToken cancellationToken)
    {
        while (true)
        {
            MessageState? state = null;
            string source = string.Empty;
            string target = string.Empty;
            GoogleTranslationClient? currentTranslator = null;
            lock (gate)
            {
                if (!IsCurrentGeneration(generation) || automaticIncomingWorkerGeneration != generation)
                {
                    return;
                }

                if (automaticIncomingQueue.Count == 0)
                {
                    automaticIncomingWorkerActive = false;
                    return;
                }

                state = automaticIncomingQueue.Dequeue();
                if (!autoIncoming || !state.IsPending || !messages.TryGetValue(state.Handle.Value, out MessageState currentState) || !ReferenceEquals(currentState, state))
                {
                    state.IsPending = false;
                    continue;
                }

                source = sourceLanguage;
                target = targetLanguage;
                currentTranslator = translator;
            }

            GoogleTranslationClient.TranslationResult? result = null;
            if (currentTranslator != null)
            {
                result = await currentTranslator.TranslateAsync(state.OriginalText, source, target, cancellationToken).ConfigureAwait(false);
            }

            bool shouldUpdate = false;
            lock (gate)
            {
                if (!IsCurrentGeneration(generation) || !messages.TryGetValue(state.Handle.Value, out MessageState currentState) || !ReferenceEquals(currentState, state))
                {
                    return;
                }

                state.IsPending = false;
                if (result != null && !SameLanguage(result.SourceLanguage, target) && !string.Equals(result.Text, state.OriginalText, StringComparison.Ordinal))
                {
                    state.Translation = result;
                    state.IsTranslated = true;
                    shouldUpdate = true;
                }
            }

            if (shouldUpdate && result != null)
            {
                UpdatePresentation(state.Handle, state.OriginalText, result, generation);
            }

            lock (gate)
            {
                if (!IsCurrentGeneration(generation) || automaticIncomingQueue.Count == 0)
                {
                    continue;
                }
            }

            await Task.Delay(AutomaticIncomingTranslationDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private void AbortAutomaticIncomingQueue(int generation)
    {
        lock (gate)
        {
            if (automaticIncomingWorkerGeneration != generation)
            {
                return;
            }

            while (automaticIncomingQueue.Count != 0)
            {
                automaticIncomingQueue.Dequeue().IsPending = false;
            }

            automaticIncomingWorkerActive = false;
        }
    }

    private async Task<ChatOutgoingMessageTransformResult> TranslateOutgoingAsync(ChatOutgoingMessageSnapshot message, CancellationToken cancellationToken)
    {
        IPluginContext? current = context;
        int generation = Volatile.Read(ref activationGeneration);
        string target;
        GoogleTranslationClient? currentTranslator = translator;
        lock (gate)
        {
            target = outgoingLanguage;
        }

        if (!IsCurrentGeneration(generation) || current == null || currentTranslator == null)
        {
            return ChatOutgoingMessageTransformResult.Fail();
        }

        // Incoming source selection belongs only to received chat. Player-authored text always
        // uses endpoint-side detection, so setting incoming Russian cannot accidentally turn an
        // outgoing translation to Russian into a Russian-to-Russian no-op.
        GoogleTranslationClient.TranslationResult? result = await currentTranslator.TranslateAsync(message.Text, "auto", target, cancellationToken).ConfigureAwait(false);
        if (!IsCurrentGeneration(generation))
        {
            return ChatOutgoingMessageTransformResult.Fail();
        }

        if (result == null || string.IsNullOrEmpty(result.Text))
        {
            return ChatOutgoingMessageTransformResult.Fail("Outgoing chat translation failed.");
        }

        if (SameLanguage(result.SourceLanguage, target))
        {
            return ChatOutgoingMessageTransformResult.Replace(message.Text);
        }

        return ChatOutgoingMessageTransformResult.Replace(result.Text);
    }

    private void UpdatePresentation(ChatMessageHandle handle, string originalText, GoogleTranslationClient.TranslationResult? translation, int generation)
    {
        IPluginContext? current = context;
        if (!IsCurrentGeneration(generation) || current == null)
        {
            return;
        }

        current.Terraria.Chat.TryUpdateMessagePresentation(handle, new ChatMessagePresentation(BuildPresentation(originalText, translation)));
    }

    private IReadOnlyList<ChatTextSpan> BuildPresentation(string originalText, GoogleTranslationClient.TranslationResult? translation)
    {
        if (translation == null)
        {
            return new[]
            {
                new ChatTextSpan(originalText, null, TranslationActionId, originalText),
                new ChatTextSpan(string.Empty, null, TranslationActionId, originalText, AttributionColor)
            };
        }

        bool showAttribution = Volatile.Read(ref showTranslationAttribution) != 0;
        return new[]
        {
            new ChatTextSpan(translation.Text, null, TranslationActionId, originalText),
            new ChatTextSpan(
                showAttribution ? " (Translated from " + LanguageName(translation.SourceLanguage) + ")" : string.Empty,
                null,
                TranslationActionId,
                originalText,
                AttributionColor)
        };
    }

    private IReadOnlyList<ChatActionMenuItem> GetMenuItems()
    {
        return Volatile.Read(ref menuItems);
    }

    private void ActivateMenuItem(string id)
    {
        if (!IsActive)
        {
            return;
        }

        if (TrySetLanguageMenuValue(id, SourceLanguagePrefix, sourceLanguageSetting))
        {
            return;
        }

        if (TrySetLanguageMenuValue(id, TargetLanguagePrefix, targetLanguageSetting))
        {
            return;
        }

        if (TrySetLanguageMenuValue(id, OutgoingLanguagePrefix, outgoingLanguageSetting))
        {
            return;
        }

        switch (id)
        {
            case "incoming":
                autoIncomingSetting!.Value = !autoIncomingSetting.Value;
                break;
            case "outgoing":
                autoOutgoingSetting!.Value = !autoOutgoingSetting.Value;
                break;
        }
    }

    private ChatActionButtonVisualState GetVisualState()
    {
        if (!IsActive)
        {
            return default;
        }

        bool incoming;
        bool outgoing;
        lock (gate)
        {
            incoming = autoIncoming;
            outgoing = autoOutgoing;
        }

        if (incoming && outgoing)
        {
            return new ChatActionButtonVisualState(IncomingBackground, OutgoingBackground);
        }

        return incoming
            ? new ChatActionButtonVisualState(IncomingBackground)
            : outgoing
                ? new ChatActionButtonVisualState(OutgoingBackground)
                : default;
    }

    private void RefreshMenuItems()
    {
        bool incoming;
        bool outgoing;
        string source;
        string target;
        string outgoingTarget;
        lock (gate)
        {
            incoming = autoIncoming;
            outgoing = autoOutgoing;
            source = sourceLanguage;
            target = targetLanguage;
            outgoingTarget = outgoingLanguage;
        }

        Volatile.Write(ref menuItems, new[]
        {
            new ChatActionMenuItem("incoming", "Auto translate incoming", EnabledText(incoming)),
            new ChatActionMenuItem("source", "Incoming translate from", sourceLanguageMenuItems, LanguageName(source), childMenuDirection: ChatActionMenuDirection.Up),
            new ChatActionMenuItem("target", "Translate to", targetLanguageMenuItems, LanguageName(target), childMenuDirection: ChatActionMenuDirection.Up),
            new ChatActionMenuItem("outgoing", "Translate outgoing", EnabledText(outgoing)),
            new ChatActionMenuItem("outgoing-target", "Outgoing language", outgoingLanguageMenuItems, LanguageName(outgoingTarget), childMenuDirection: ChatActionMenuDirection.Up)
        });
    }

    private void ClearSession()
    {
        lock (gate)
        {
            messages.Clear();
            automaticIncomingQueue.Clear();
        }

        translator?.Clear();
    }

    private bool IsActive => Volatile.Read(ref active) != 0;

    private bool IsCurrentGeneration(int generation)
    {
        return IsActive && Volatile.Read(ref activationGeneration) == generation;
    }

    private bool CanTransformOutgoing(ChatOutgoingMessageSnapshot message)
    {
        if (!IsActive || message == null || message.IsCommand || !CanTranslateText(message.Text))
        {
            return false;
        }

        lock (gate)
        {
            return autoOutgoing;
        }
    }

    private void SetAutoIncoming(bool value)
    {
        lock (gate)
        {
            autoIncoming = value;
        }

        RefreshMenuItems();
    }

    private void SetAutoOutgoing(bool value)
    {
        lock (gate)
        {
            autoOutgoing = value;
        }

        RefreshMenuItems();
    }

    private void SetShowTranslationAttribution(bool value)
    {
        Volatile.Write(ref showTranslationAttribution, value ? 1 : 0);

        List<MessageState>? translated = null;
        lock (gate)
        {
            foreach (MessageState state in messages.Values)
            {
                if (state.IsTranslated && state.Translation != null)
                {
                    translated ??= new List<MessageState>();
                    translated.Add(state);
                }
            }
        }

        if (translated == null)
        {
            return;
        }

        int generation = Volatile.Read(ref activationGeneration);
        foreach (MessageState state in translated)
        {
            UpdatePresentation(state.Handle, state.OriginalText, state.Translation, generation);
        }
    }

    private void SetSourceLanguage(string value)
    {
        lock (gate)
        {
            sourceLanguage = value;
        }

        RefreshMenuItems();
    }

    private void SetTargetLanguage(string value)
    {
        lock (gate)
        {
            targetLanguage = value;
        }

        RefreshMenuItems();
    }

    private void SetOutgoingLanguage(string value)
    {
        lock (gate)
        {
            outgoingLanguage = value;
        }

        RefreshMenuItems();
    }

    private void TrimMessages()
    {
        if (messages.Count <= 512)
        {
            return;
        }

        long oldest = long.MaxValue;
        foreach (long handle in messages.Keys)
        {
            if (handle < oldest)
            {
                oldest = handle;
            }
        }

        messages.Remove(oldest);
    }

    private static bool ContainsLink(IReadOnlyList<ChatTextSpan> spans)
    {
        for (int index = 0; index < spans.Count; index++)
        {
            if (!string.IsNullOrEmpty(spans[index].LinkTarget))
            {
                return true;
            }
        }

        return false;
    }

    private static string Concatenate(IReadOnlyList<ChatTextSpan> spans)
    {
        if (spans.Count == 1)
        {
            return spans[0].Text;
        }

        var builder = new StringBuilder();
        for (int index = 0; index < spans.Count; index++)
        {
            builder.Append(spans[index].Text);
        }

        return builder.ToString();
    }

    private static bool CanTranslateText(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Length <= 4500;
    }

    // Only reject a request when a configured source language has an unmistakable writing system
    // that is absent from the detached chat text. Latin-script languages deliberately remain
    // eligible: distinguishing English, French, Spanish, and similar languages locally would be
    // guesswork and could hide a valid translation.
    private static bool CanPossiblyMatchIncomingSource(string text, string source)
    {
        switch (NormalizeLanguageCode(source))
        {
            case "ru":
            case "uk":
            case "be":
            case "bg":
            case "mk":
            case "sr":
                return ContainsCharacterInRange(text, (char)0x0400, (char)0x052F);
            case "el":
                return ContainsCharacterInRange(text, (char)0x0370, (char)0x03FF);
            case "ar":
            case "fa":
            case "ur":
                return ContainsCharacterInRange(text, (char)0x0600, (char)0x06FF);
            case "he":
                return ContainsCharacterInRange(text, (char)0x0590, (char)0x05FF);
            case "hi":
            case "mr":
            case "ne":
                return ContainsCharacterInRange(text, (char)0x0900, (char)0x097F);
            case "ja":
                return ContainsCharacterInRange(text, (char)0x3040, (char)0x30FF) || ContainsCharacterInRange(text, (char)0x4E00, (char)0x9FFF);
            case "ko":
                return ContainsCharacterInRange(text, (char)0xAC00, (char)0xD7AF);
            case "zh":
            case "zh-cn":
            case "zh-tw":
                return ContainsCharacterInRange(text, (char)0x3400, (char)0x9FFF);
            default:
                return true;
        }
    }

    private static bool ContainsCharacterInRange(string text, char minimum, char maximum)
    {
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character >= minimum && character <= maximum)
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<PluginSettingOption> GetSourceLanguageOptions()
    {
        return TranslationLanguageCatalog.DefaultSources;
    }

    private IReadOnlyList<PluginSettingOption> GetTargetLanguageOptions()
    {
        return TranslationLanguageCatalog.DefaultTargets;
    }

    private static ChatActionMenuItem[] CreateLanguageMenuItems(string prefix, IReadOnlyList<PluginSettingOption> options)
    {
        var items = new ChatActionMenuItem[options.Count];
        for (int index = 0; index < options.Count; index++)
        {
            PluginSettingOption option = options[index];
            items[index] = new ChatActionMenuItem(prefix + option.Value, option.DisplayName);
        }

        return items;
    }

    private static bool TrySetLanguageMenuValue(string id, string prefix, IPluginSetting<string>? setting)
    {
        if (setting == null || !id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        setting.Value = id.Substring(prefix.Length);
        return true;
    }

    private static string NormalizeSourceLanguage(string value)
    {
        string normalized = NormalizeLanguageCode(value);
        return TranslationLanguageCatalog.IsSupportedSource(normalized) ? normalized : "auto";
    }

    private static string NormalizeTargetLanguage(string value)
    {
        string normalized = NormalizeLanguageCode(value);
        return TranslationLanguageCatalog.IsSupportedTarget(normalized) ? normalized : "en";
    }

    private static string NormalizeLanguageCode(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool SameLanguage(string source, string target)
    {
        string normalizedSource = NormalizeLanguageCode(source);
        string normalizedTarget = NormalizeLanguageCode(target);
        return TranslationLanguageCatalog.IsSupportedSource(normalizedSource) &&
            TranslationLanguageCatalog.IsSupportedTarget(normalizedTarget) &&
            string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal);
    }

    private string LanguageName(string code)
    {
        string normalized = NormalizeLanguageCode(code);
        string? name = FindLanguageName(TranslationLanguageCatalog.DefaultSources, normalized);
        if (name != null)
        {
            return name;
        }

        name = FindLanguageName(TranslationLanguageCatalog.DefaultTargets, normalized);
        return name ?? normalized;
    }

    private static string? FindLanguageName(IReadOnlyList<PluginSettingOption> options, string value)
    {
        for (int index = 0; index < options.Count; index++)
        {
            PluginSettingOption option = options[index];
            if (string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return option.DisplayName;
            }
        }

        return null;
    }

    private static string EnabledText(bool value)
    {
        return value ? "Enabled" : "Disabled";
    }

    private sealed class IncomingDecorator : IChatSpanDecorator
    {
        private readonly ChatTranslationPlugin plugin;

        internal IncomingDecorator(ChatTranslationPlugin plugin)
        {
            this.plugin = plugin;
        }

        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message)
        {
            return plugin.Decorate(message, new[] { new ChatTextSpan(message.Text) });
        }

        public IReadOnlyList<ChatTextSpan> Decorate(ChatMessageSnapshot message, IReadOnlyList<ChatTextSpan> currentSpans)
        {
            return plugin.Decorate(message, currentSpans);
        }
    }

    private sealed class MessageAction : IChatMessageActionHandler
    {
        private readonly ChatTranslationPlugin plugin;

        internal MessageAction(ChatTranslationPlugin plugin)
        {
            this.plugin = plugin;
        }

        public bool TryActivate(ChatMessageActionInvocation invocation)
        {
            return plugin.ToggleMessage(invocation);
        }
    }

    private sealed class ActionButton : IChatActionButtonHandler
    {
        private readonly ChatTranslationPlugin plugin;

        internal ActionButton(ChatTranslationPlugin plugin)
        {
            this.plugin = plugin;
        }

        public void Activate(ChatActionButtonInvocation invocation)
        {
            if (invocation.Shift && plugin.IsActive)
            {
                if (invocation.Button == ChatActionButtonMouseButton.Left)
                {
                    plugin.autoOutgoingSetting!.Value = !plugin.autoOutgoingSetting.Value;
                }
                else
                {
                    plugin.autoIncomingSetting!.Value = !plugin.autoIncomingSetting.Value;
                }
            }
        }

        public IReadOnlyList<ChatActionMenuItem> GetMenuItems()
        {
            return plugin.GetMenuItems();
        }

        public void ActivateMenuItem(string id)
        {
            plugin.ActivateMenuItem(id);
        }

        public ChatActionButtonVisualState GetVisualState()
        {
            return plugin.GetVisualState();
        }
    }

    private sealed class OutgoingTransformer : IChatOutgoingMessageTransformer
    {
        private readonly ChatTranslationPlugin plugin;

        internal OutgoingTransformer(ChatTranslationPlugin plugin)
        {
            this.plugin = plugin;
        }

        public bool CanTransform(ChatOutgoingMessageSnapshot message)
        {
            return plugin.CanTransformOutgoing(message);
        }

        public Task<ChatOutgoingMessageTransformResult> TransformAsync(ChatOutgoingMessageSnapshot message, CancellationToken cancellationToken)
        {
            return plugin.TranslateOutgoingAsync(message, cancellationToken);
        }
    }

    private sealed class MessageState
    {
        internal MessageState(ChatMessageHandle handle, string originalText)
        {
            Handle = handle;
            OriginalText = originalText;
        }

        internal ChatMessageHandle Handle { get; }
        internal string OriginalText { get; }
        internal bool IsPending { get; set; }
        internal bool IsTranslated { get; set; }
        internal bool AutomaticAttempted { get; set; }
        internal GoogleTranslationClient.TranslationResult? Translation { get; set; }
    }
}
