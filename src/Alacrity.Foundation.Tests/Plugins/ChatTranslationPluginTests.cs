using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.ChatTranslation;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class ChatTranslationPluginTests
{
    private const string TranslatePaResponse = "{\"translations\":[{\"detectedLanguageCode\":\"es\",\"translatedText\":\"hello\"}]}";

    [Fact]
    public async Task ManualIncomingTranslation_UsesScopedNetworkAndRestoresOriginalPresentation()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        var handle = new ChatMessageHandle(10);
        var updateCompletion = new TaskCompletionSource<ChatMessageHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Chat.MessagePresentationUpdated += message => updateCompletion.TrySetResult(message);
        var original = host.Chat.Decorate(new ChatMessageSnapshot("hola", handle));
        Assert.Equal("hola", original[0].Text);
        Assert.True(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));

        ChatMessagePresentation presentation = await WaitForPresentation(host.Chat, handle);
        Assert.Equal("hello", presentation.Spans[0].Text);
        Assert.Equal(" (Translated from Spanish)", presentation.Spans[1].Text);
        Assert.Equal(handle, await updateCompletion.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        PluginWebRequest translationRequest = Assert.Single(host.NetworkBackend.Requests, request => request.Method == PluginWebRequestMethod.Get);
        Assert.Equal("translate-pa.googleapis.com", translationRequest.Uri.Host);
        Assert.Contains("params.client=gtx", translationRequest.Uri.Query);
        Assert.Contains("dataTypes=TRANSLATION", translationRequest.Uri.Query);
        Assert.Contains("query.sourceLanguage=auto", translationRequest.Uri.Query);
        Assert.Contains("query.targetLanguage=en", translationRequest.Uri.Query);
        Assert.Contains("query.text=hola", translationRequest.Uri.Query);

        Assert.True(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));
        Assert.True(host.Chat.TryGetMessagePresentation(handle, out ChatMessagePresentation? restored, out _));
        Assert.NotNull(restored);
        Assert.Equal("hola", restored!.Spans[0].Text);

        // A native retained line may be rewrapped after presentation changes. The same stable
        // handle must rebuild from the original local message instead of restoring a stale
        // translated state.
        IReadOnlyList<ChatTextSpan> rebuilt = host.Chat.Decorate(new ChatMessageSnapshot("hola", handle));
        Assert.Equal("hola", rebuilt[0].Text);

        context.Resources.Dispose();
        Assert.False(host.Chat.HasActionButtons);
        Assert.False(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));
    }

    [Fact]
    public async Task AutomaticIncomingTranslation_PublishesTheSameOwnedPresentation()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoIncoming", true);

        var handle = new ChatMessageHandle(11);
        IReadOnlyList<ChatTextSpan> initial = host.Chat.Decorate(new ChatMessageSnapshot("hola", handle));

        Assert.Equal("hola", initial[0].Text);
        ChatMessagePresentation presentation = await WaitForPresentation(host.Chat, handle);
        Assert.Equal("hello", presentation.Spans[0].Text);
        Assert.Equal(" (Translated from Spanish)", presentation.Spans[1].Text);
        Assert.Single(host.NetworkBackend.Requests);
    }

    [Fact]
    public async Task TranslationAttributionSetting_UpdatesExistingPresentationWithoutChangingItsShape()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        var handle = new ChatMessageHandle(16);
        host.Chat.Decorate(new ChatMessageSnapshot("hola", handle));
        Assert.True(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));
        ChatMessagePresentation shown = await WaitForPresentation(host.Chat, handle);
        Assert.Equal(" (Translated from Spanish)", shown.Spans[1].Text);

        context.Settings.Set("showTranslationAttribution", false);

        Assert.True(host.Chat.TryGetMessagePresentation(handle, out ChatMessagePresentation? hidden, out _));
        Assert.NotNull(hidden);
        Assert.Equal(2, hidden!.Spans.Count);
        Assert.Equal(string.Empty, hidden.Spans[1].Text);
    }

    [Fact]
    public async Task RetainedMessageRewrap_UsesOneTranslationRequestForTheCompleteMessage()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoIncoming", true);

        const string message = "hola this complete retained chat message must not be split for translation";
        var handle = new ChatMessageHandle(12);
        host.Chat.Decorate(new ChatMessageSnapshot(message, handle));
        // Terraria can rebuild wrapped display snippets after a size change or presentation
        // update. Reusing the retained-message handle must not issue a request per fragment.
        host.Chat.Decorate(new ChatMessageSnapshot(message, handle));

        await WaitForPresentation(host.Chat, handle);
        PluginWebRequest request = Assert.Single(host.NetworkBackend.Requests);
        Assert.Contains("query.text=hola%20this%20complete%20retained%20chat%20message%20must%20not%20be%20split%20for%20translation", request.Uri.Query);
    }

    [Fact]
    public async Task AutomaticIncomingTranslation_ProcessesRetainedMessagesOneAtATime()
    {
        using var host = new FakePluginHost();
        var firstResponse = new TaskCompletionSource<PluginWebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.NetworkBackend.Handler = (_, _) => firstResponse.Task;
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoIncoming", true);

        host.Chat.Decorate(new ChatMessageSnapshot("hola one", new ChatMessageHandle(61)));
        host.Chat.Decorate(new ChatMessageSnapshot("hola two", new ChatMessageHandle(62)));
        await WaitUntil(() => host.NetworkBackend.Requests.Count == 1);
        Assert.Single(host.NetworkBackend.Requests);

        firstResponse.TrySetResult(new PluginWebResponse(200, TranslatePaResponse));
        await WaitUntil(() => host.NetworkBackend.Requests.Count == 2);
        Assert.True(host.Chat.TryGetMessagePresentation(new ChatMessageHandle(61), out ChatMessagePresentation? first, out _));
        Assert.NotNull(first);
        Assert.Equal("hello", first!.Spans[0].Text);
    }

    [Fact]
    public async Task IncomingTranslation_LeavesMessagesAlreadyInTheTargetLanguageUntouched()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, "{\"translations\":[{\"detectedLanguageCode\":\"en\",\"translatedText\":\"hello\"}]}"));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        var handle = new ChatMessageHandle(63);
        host.Chat.Decorate(new ChatMessageSnapshot("hello", handle));
        Assert.True(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));
        await WaitUntil(() => host.NetworkBackend.Requests.Count == 1);
        await Task.Delay(25);
        Assert.False(host.Chat.TryGetMessagePresentation(handle, out _, out _));
    }

    [Fact]
    public async Task IncomingTranslation_ConfiguredForRussianSkipsEnglishWithoutMakingARequest()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("sourceLanguage", "ru");
        context.Settings.Set("autoIncoming", true);

        host.Chat.Decorate(new ChatMessageSnapshot("plain English chat", new ChatMessageHandle(65)));
        await Task.Delay(25);

        Assert.Empty(host.NetworkBackend.Requests);
    }

    [Fact]
    public async Task OutgoingTranslation_IsDeferredOnceAndSubmitsTheReadyReplacementOnlyOnce()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoOutgoing", true);

        Assert.True(host.Chat.TryDeferOutgoingMessage("hola"));
        string replacement = await WaitForOutgoingReplacement(host.Chat);
        Assert.Equal("hello", replacement);
        Assert.False(host.Chat.TryDeferOutgoingMessage(replacement));
        Assert.False(host.Chat.TryDeferOutgoingMessage("/native-command"));

        context.Resources.Dispose();
        Assert.False(host.Chat.TryDeferOutgoingMessage("hola"));
    }

    [Fact]
    public async Task OutgoingTranslation_AutoDetectsIndependentlyOfIncomingSourceLanguage()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("sourceLanguage", "ru");
        context.Settings.Set("outgoingLanguage", "ru");
        context.Settings.Set("autoOutgoing", true);

        Assert.True(host.Chat.TryDeferOutgoingMessage("hello"));
        Assert.Equal("hello", await WaitForOutgoingReplacement(host.Chat));
        PluginWebRequest request = Assert.Single(host.NetworkBackend.Requests);
        Assert.Contains("query.sourceLanguage=auto", request.Uri.Query);
        Assert.Contains("query.targetLanguage=ru", request.Uri.Query);
    }

    [Fact]
    public async Task AutomaticIncomingWork_DoesNotBlockOutgoingTranslation()
    {
        using var host = new FakePluginHost();
        var incomingStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseIncoming = new TaskCompletionSource<PluginWebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.NetworkBackend.Handler = (request, _) =>
        {
            if (request.Uri.Query.Contains("query.text=incoming", StringComparison.Ordinal))
            {
                incomingStarted.TrySetResult(null);
                return releaseIncoming.Task;
            }

            return Task.FromResult(new PluginWebResponse(200, TranslatePaResponse));
        };
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoIncoming", true);
        context.Settings.Set("autoOutgoing", true);

        host.Chat.Decorate(new ChatMessageSnapshot("incoming", new ChatMessageHandle(64)));
        await incomingStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(host.Chat.TryDeferOutgoingMessage("hola"));
        Assert.Equal("hello", await WaitForOutgoingReplacement(host.Chat));
        releaseIncoming.TrySetResult(new PluginWebResponse(200, TranslatePaResponse));
    }

    [Fact]
    public async Task FlatTranslatePaResponse_RemainsSupportedForOutgoingMessages()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, "{\"sourceLanguage\":\"es\",\"translation\":\"hello\"}"));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoOutgoing", true);

        Assert.True(host.Chat.TryDeferOutgoingMessage("hola"));
        Assert.Equal("hello", await WaitForOutgoingReplacement(host.Chat));
    }

    [Fact]
    public async Task NestedDataTranslateResponse_RemainsSupportedForOutgoingMessages()
    {
        using var host = new FakePluginHost();
        host.NetworkBackend.Handler = (_, _) => Task.FromResult(new PluginWebResponse(200, "{\"data\":{\"translations\":[{\"detectedLanguageCode\":\"es\",\"translatedText\":\"hello\"}]}}"));
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();
        context.Settings.Set("autoOutgoing", true);

        Assert.True(host.Chat.TryDeferOutgoingMessage("hola"));
        Assert.Equal("hello", await WaitForOutgoingReplacement(host.Chat));
    }

    [Fact]
    public void ActionButtonBackground_ComposesIncomingAndOutgoingState()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        Assert.True(host.Chat.TryGetActionButtonVisualState(manifest.Id, "translation", out ChatActionButtonVisualState initial));
        Assert.False(initial.PrimaryBackground.HasValue);

        context.Settings.Set("autoIncoming", true);
        Assert.True(host.Chat.TryGetActionButtonVisualState(manifest.Id, "translation", out ChatActionButtonVisualState incoming));
        Assert.True(incoming.PrimaryBackground.HasValue);
        Assert.False(incoming.SecondaryBackground.HasValue);

        context.Settings.Set("autoOutgoing", true);
        Assert.True(host.Chat.TryGetActionButtonVisualState(manifest.Id, "translation", out ChatActionButtonVisualState both));
        Assert.True(both.PrimaryBackground.HasValue);
        Assert.True(both.SecondaryBackground.HasValue);
    }

    [Fact]
    public void ShiftActionButtonClicks_ToggleTheRequestedTranslationDirection()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        Assert.True(host.Chat.TryActivateActionButton(manifest.Id, "translation", ChatActionButtonMouseButton.Left, shift: true));
        Assert.True(context.Settings.Get("autoOutgoing", false));
        Assert.False(context.Settings.Get("autoIncoming", false));

        Assert.True(host.Chat.TryActivateActionButton(manifest.Id, "translation", ChatActionButtonMouseButton.Right, shift: true));
        Assert.True(context.Settings.Get("autoOutgoing", false));
        Assert.True(context.Settings.Get("autoIncoming", false));
    }

    [Fact]
    public void LanguageSettingsAndActionMenuUseHostOwnedDropdownChoices()
    {
        using var host = new FakePluginHost();
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        IReadOnlyList<PluginSettingControl> controls = host.GetSettingsControls(manifest.Id);
        Assert.Contains(controls, control => control.Kind == PluginSettingControlKind.Dropdown && control.Id == "source-language");
        Assert.Contains(controls, control => control.Kind == PluginSettingControlKind.Dropdown && control.Id == "target-language");
        Assert.Contains(controls, control => control.Kind == PluginSettingControlKind.Dropdown && control.Id == "outgoing-language");

        IReadOnlyList<ChatActionMenuItem> items = host.Chat.GetActionButtonMenuItems(manifest.Id, "translation");
        ChatActionMenuItem source = Assert.Single(items, item => item.Id == "source");
        Assert.True(source.HasChildren);
        Assert.Contains(source.Children, item => item.Id == "source-language:fr");
        Assert.True(source.Children.Count > 100);

        Assert.True(host.Chat.TryActivateActionButtonMenuItem(manifest.Id, "translation", "source-language:fr"));
        Assert.Equal("fr", context.Settings.Get("sourceLanguage", string.Empty));
        Assert.DoesNotContain(items, item => item.Id == "paste-key");
    }

    [Fact]
    public async Task DisableDuringRequest_PreventsOldActivationFromPublishingPresentation()
    {
        using var host = new FakePluginHost();
        var release = new TaskCompletionSource<PluginWebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.NetworkBackend.Handler = async (_, cancellationToken) => await release.Task.WaitAsync(cancellationToken);
        PluginManifest manifest = CreateManifest();
        var context = host.Create(manifest);
        var plugin = new ChatTranslationPlugin();
        plugin.Initialize(context);
        plugin.Enable();

        var handle = new ChatMessageHandle(42);
        host.Chat.Decorate(new ChatMessageSnapshot("hola", handle));
        Assert.True(host.Chat.TryActivateMessageAction(manifest.Id, "toggle-translation", handle, "toggle", false));
        await WaitUntil(() => host.NetworkBackend.Requests.Count == 1);

        plugin.Disable();
        release.TrySetResult(new PluginWebResponse(200, TranslatePaResponse));
        await Task.Delay(25);
        Assert.False(host.Chat.TryGetMessagePresentation(handle, out _, out _));
    }

    private static async Task<ChatMessagePresentation> WaitForPresentation(PluginChatHost host, ChatMessageHandle handle)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (host.TryGetMessagePresentation(handle, out ChatMessagePresentation? presentation, out _) && presentation != null)
            {
                return presentation;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("The asynchronous chat presentation did not complete.");
    }

    private static async Task<string> WaitForOutgoingReplacement(PluginChatHost host)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (host.TryTakeReadyOutgoingMessage(out string replacement))
            {
                return replacement;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("The outgoing translation did not complete.");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new Xunit.Sdk.XunitException("The expected asynchronous operation did not start.");
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("alacrity.chat-translation"),
            "Chat Translation",
            new Version(0, 1),
            "Tests",
            "Tests generic chat translation.",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.UserInterface | PluginCapability.Networking,
            permissions: PluginPermission.DrawUserInterface | PluginPermission.Clipboard | PluginPermission.NetworkAccess,
            multiplayerSafety: MultiplayerSafety.ClientOnly,
            networkHosts: new[] { "translate-pa.googleapis.com" });
    }
}
