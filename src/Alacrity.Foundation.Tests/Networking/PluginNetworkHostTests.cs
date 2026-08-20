using System;
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

    private sealed class ImmediateNetworkBackend : IPluginNetworkBackend
    {
        public Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PluginWebResponse(200, "{}"));
        }
    }

    private sealed class SilentLogger : IPluginLogger
    {
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
