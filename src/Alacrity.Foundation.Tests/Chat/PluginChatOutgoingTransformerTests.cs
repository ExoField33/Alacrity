using System;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginChatOutgoingTransformerTests
{
    [Fact]
    public async Task TimedOutTransformReleasesTheInputForLaterNativeSubmission()
    {
        using var host = new FakePluginHost();
        var context = host.Create(CreateManifest());
        var transformer = new ControlledTransformer("first");
        context.Terraria.Chat.RegisterOutgoingMessageTransformer(new ChatOutgoingMessageTransformerDescriptor("controlled"), transformer);

        TimeSpan previous = Alacrity.Core.PluginChatHost.OutgoingTransformTimeout;
        Alacrity.Core.PluginChatHost.OutgoingTransformTimeout = TimeSpan.FromMilliseconds(25);
        try
        {
            Assert.True(host.Chat.TryDeferOutgoingMessage("first"));
            await transformer.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.Delay(75);

            Assert.False(host.Chat.TryTakeReadyOutgoingMessage(out _));
            Assert.False(host.Chat.TryDeferOutgoingMessage("later"));
            Assert.Contains(host.Diagnostics, entry => entry.Contains("timed out", StringComparison.Ordinal));
        }
        finally
        {
            transformer.Complete(ChatOutgoingMessageTransformResult.Replace("first translated"));
            Alacrity.Core.PluginChatHost.OutgoingTransformTimeout = previous;
        }
    }

    [Fact]
    public async Task EditedInputRevokesAStaleTransformBeforeItCanPublish()
    {
        using var host = new FakePluginHost();
        var context = host.Create(CreateManifest());
        var transformer = new ControlledTransformer("first");
        context.Terraria.Chat.RegisterOutgoingMessageTransformer(new ChatOutgoingMessageTransformerDescriptor("controlled"), transformer);

        Assert.True(host.Chat.TryDeferOutgoingMessage("first"));
        await transformer.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        host.Chat.ObserveOutgoingInput("new player text");
        transformer.Complete(ChatOutgoingMessageTransformResult.Replace("stale replacement"));
        await Task.Delay(25);

        Assert.False(host.Chat.TryTakeReadyOutgoingMessage(out _));
        Assert.False(host.Chat.TryDeferOutgoingMessage("new player text"));
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("alacrity.chat-host-tests"),
            "Chat Host Tests",
            new Version(1, 0),
            "Tests",
            "Tests outgoing transform boundaries.",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Networking,
            permissions: PluginPermission.NetworkAccess,
            multiplayerSafety: MultiplayerSafety.ClientOnly,
            networkHosts: new[] { "example.invalid" });
    }

    private sealed class ControlledTransformer : IChatOutgoingMessageTransformer
    {
        private readonly string acceptedText;
        private readonly TaskCompletionSource<ChatOutgoingMessageTransformResult> completion =
            new TaskCompletionSource<ChatOutgoingMessageTransformResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ControlledTransformer(string acceptedText)
        {
            this.acceptedText = acceptedText;
        }

        internal TaskCompletionSource<object?> Started { get; } =
            new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanTransform(ChatOutgoingMessageSnapshot message)
        {
            return string.Equals(message.Text, acceptedText, StringComparison.Ordinal);
        }

        public Task<ChatOutgoingMessageTransformResult> TransformAsync(ChatOutgoingMessageSnapshot message, CancellationToken cancellationToken)
        {
            Started.TrySetResult(null);
            return completion.Task;
        }

        internal void Complete(ChatOutgoingMessageTransformResult result)
        {
            completion.TrySetResult(result);
        }
    }
}
