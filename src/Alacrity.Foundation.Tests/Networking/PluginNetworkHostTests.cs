using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.Core;
using Alacrity.PluginSdk;
using Xunit;

public sealed class PluginNetworkHostTests
{
    [Fact]
    public async Task ScopedNetworkService_EnforcesManifestHostAndActivationLifetime()
    {
        var backend = new ImmediateNetworkBackend();
        var host = new PluginNetworkHost(backend);
        using var scope = new PluginResourceScope();
        PluginManifest manifest = new PluginManifest(
            new PluginId("network.test"),
            "Network Test",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Networking,
            permissions: PluginPermission.NetworkAccess,
            networkHosts: new[] { "example.test" });
        IPluginNetworkService service = host.CreateService(manifest, scope, new SilentLogger());

        PluginWebResponse response = await service.SendAsync(new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://example.test/translation")), CancellationToken.None);
        Assert.True(response.IsSuccessStatusCode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SendAsync(new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://untrusted.example/")), CancellationToken.None));

        scope.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SendAsync(new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://example.test/translation")), CancellationToken.None));
    }

    [Fact]
    public async Task ScopedNetworkService_RejectsRedirectToAnUnapprovedHost()
    {
        var backend = new RedirectBackend(new Uri("https://untrusted.test/redirected"));
        var host = new PluginNetworkHost(backend);
        using var scope = new PluginResourceScope();
        IPluginNetworkService service = host.CreateService(CreateManifest(), scope, new SilentLogger());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SendAsync(new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://example.test/start")), CancellationToken.None));
        Assert.Equal(1, backend.CallCount);
    }

    [Fact]
    public async Task HttpBackend_StopsUnknownLengthResponseAfterTheBoundedLimit()
    {
        var stream = new CountingStream(PluginNetworkHost.MaximumResponseBytes + 4096);
        using var backend = new HttpPluginNetworkBackend(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            backend.SendAsync(new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://example.test/large")), CancellationToken.None));
        Assert.Equal(PluginNetworkHost.MaximumResponseBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task ScopedNetworkService_ActivationReleaseCancelsAnInFlightRequest()
    {
        var backend = new CancellationAwareBackend();
        var host = new PluginNetworkHost(backend);
        using var scope = new PluginResourceScope();
        IPluginNetworkService service = host.CreateService(CreateManifest(), scope, new SilentLogger());

        Task<PluginWebResponse> request = service.SendAsync(
            new PluginWebRequest(PluginWebRequestMethod.Get, new Uri("https://example.test/slow")),
            CancellationToken.None);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        scope.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(backend.Cancelled);
    }

    private static PluginManifest CreateManifest()
    {
        return new PluginManifest(
            new PluginId("network.test"),
            "Network Test",
            new Version(1, 0),
            "Tests",
            "Tests",
            new[] { "1.4.5.6" },
            capabilities: PluginCapability.Networking,
            permissions: PluginPermission.NetworkAccess,
            networkHosts: new[] { "example.test" });
    }

    private sealed class ImmediateNetworkBackend : IPluginNetworkBackend
    {
        public Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PluginWebResponse(200, "{}"));
        }
    }

    private sealed class RedirectBackend : IPluginNetworkBackend
    {
        private readonly Uri redirect;

        internal RedirectBackend(Uri redirect)
        {
            this.redirect = redirect;
        }

        internal int CallCount { get; private set; }

        public Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PluginWebResponse(302, string.Empty, redirect));
        }
    }

    private sealed class CancellationAwareBackend : IPluginNetworkBackend
    {
        private readonly TaskCompletionSource<PluginWebResponse> completion = new TaskCompletionSource<PluginWebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<object?> Started { get; } = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool Cancelled { get; private set; }

        public async Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult(null);
            using (cancellationToken.Register(() =>
            {
                Cancelled = true;
                completion.TrySetCanceled(cancellationToken);
            }))
            {
                return await completion.Task.ConfigureAwait(false);
            }
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage response;

        internal StaticResponseHandler(HttpResponseMessage response)
        {
            this.response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class CountingStream : Stream
    {
        private readonly int length;

        internal CountingStream(int length)
        {
            this.length = length;
        }

        internal int BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int remaining = length - BytesRead;
            if (remaining <= 0)
            {
                return 0;
            }

            int read = Math.Min(remaining, count);
            Array.Clear(buffer, offset, read);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SilentLogger : IPluginLogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
