using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Host-owned bounded HTTPS transport. Plugins receive scoped request values rather than
/// a mutable <see cref="HttpClient"/>, arbitrary headers, or direct socket access.</summary>
public sealed class PluginNetworkHost
{
    private const int MaximumRequestCharacters = 32 * 1024;
    private const int MaximumResponseCharacters = 128 * 1024;
    private readonly IPluginNetworkBackend backend;

    /// <summary>Creates the production network host.</summary>
    public PluginNetworkHost()
        : this(HttpPluginNetworkBackend.Instance)
    {
    }

    /// <summary>Creates a host with a testable transport backend.</summary>
    public PluginNetworkHost(IPluginNetworkBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>Creates an activation-scoped, manifest-guarded network service.</summary>
    public IPluginNetworkService CreateService(PluginManifest manifest, IPluginResourceScope resources, IPluginLogger logger)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (resources == null) throw new ArgumentNullException(nameof(resources));
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        var guard = new NetworkScopeGuard();
        try
        {
            resources.Own("network", PluginResourceKind.BackgroundTask, guard);
        }
        catch
        {
            guard.Dispose();
            throw;
        }

        return new ScopedNetworkService(manifest, backend, logger, guard);
    }

    private sealed class ScopedNetworkService : IPluginNetworkService
    {
        private readonly PluginManifest manifest;
        private readonly IPluginNetworkBackend backend;
        private readonly IPluginLogger logger;
        private readonly NetworkScopeGuard guard;

        internal ScopedNetworkService(PluginManifest manifest, IPluginNetworkBackend backend, IPluginLogger logger, NetworkScopeGuard guard)
        {
            this.manifest = manifest;
            this.backend = backend;
            this.logger = logger;
            this.guard = guard;
        }

        public async Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
        {
            ThrowIfClosed();
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsurePermission();
            ValidateRequest(request);

            try
            {
                PluginWebResponse response = await backend.SendAsync(request, cancellationToken).ConfigureAwait(false);
                ThrowIfClosed();
                if (response.Content.Length > MaximumResponseCharacters)
                {
                    throw new InvalidOperationException("The remote response exceeded the host response limit.");
                }

                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || guard.IsReleased)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.Error("Network request failed for plugin '" + manifest.Id.Value + "' to host '" + request.Uri.Host + "'.", exception);
                throw;
            }
        }

        private void EnsurePermission()
        {
            if ((manifest.Capabilities & PluginCapability.Networking) != PluginCapability.Networking ||
                (manifest.Permissions & PluginPermission.NetworkAccess) != PluginPermission.NetworkAccess)
            {
                throw new UnauthorizedAccessException("Plugin '" + manifest.Id.Value + "' must declare Networking and NetworkAccess before sending HTTPS requests.");
            }
        }

        private void ValidateRequest(PluginWebRequest request)
        {
            if (request.Content.Length > MaximumRequestCharacters)
            {
                throw new InvalidOperationException("The request body exceeded the host request limit.");
            }

            string host = request.Uri.Host.TrimEnd('.').ToLowerInvariant();
            bool allowed = false;
            for (int index = 0; index < manifest.NetworkHosts.Count; index++)
            {
                if (string.Equals(manifest.NetworkHosts[index], host, StringComparison.Ordinal))
                {
                    allowed = true;
                    break;
                }
            }

            if (!allowed)
            {
                throw new UnauthorizedAccessException("Plugin '" + manifest.Id.Value + "' is not approved to contact HTTPS host '" + host + "'.");
            }
        }

        private void ThrowIfClosed()
        {
            if (guard.IsReleased)
            {
                throw new ObjectDisposedException(nameof(IPluginNetworkService), "The owning plugin activation has been released.");
            }
        }
    }

    private sealed class NetworkScopeGuard : IDisposable
    {
        private int released;

        internal bool IsReleased => Volatile.Read(ref released) != 0;

        public void Dispose()
        {
            Interlocked.Exchange(ref released, 1);
        }
    }
}

/// <summary>Test seam for the host-owned web transport.</summary>
public interface IPluginNetworkBackend
{
    /// <summary>Sends one validated request.</summary>
    Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken);
}

internal sealed class HttpPluginNetworkBackend : IPluginNetworkBackend
{
    internal static readonly HttpPluginNetworkBackend Instance = new HttpPluginNetworkBackend();
    private static readonly HttpClient Client = new HttpClient();

    private HttpPluginNetworkBackend()
    {
    }

    public async Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
    {
        using (var message = new HttpRequestMessage(request.Method == PluginWebRequestMethod.Get ? HttpMethod.Get : HttpMethod.Post, request.Uri))
        {
            if (request.Method == PluginWebRequestMethod.Post)
            {
                message.Content = new StringContent(request.Content, Encoding.UTF8, request.ContentType);
            }

            using (HttpResponseMessage response = await Client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
            {
                string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new PluginWebResponse((int)response.StatusCode, content);
            }
        }
    }
}

/// <summary>Unavailable network service used by hosts that intentionally provide no transport.</summary>
public sealed class UnsupportedPluginNetworkService : IPluginNetworkService
{
    /// <summary>Shared unavailable service.</summary>
    public static readonly UnsupportedPluginNetworkService Instance = new UnsupportedPluginNetworkService();

    private UnsupportedPluginNetworkService()
    {
    }

    /// <inheritdoc />
    public Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Host-managed networking is unavailable in this host.");
    }
}
