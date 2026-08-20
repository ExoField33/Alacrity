using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alacrity.PluginSdk;

/// <summary>
/// Sends bounded HTTPS requests through a host-owned transport. Requests are allowed only for
/// hosts declared by the verified plugin manifest, and completion never grants access to mutable
/// game state. Callers should run non-interactive requests through <see cref="IPluginScheduler"/>
/// background work and marshal presentation updates through <see cref="IPluginDispatcher"/>.
/// </summary>
public interface IPluginNetworkService
{
    /// <summary>Sends one validated request for the current activation.</summary>
    Task<PluginWebResponse> SendAsync(PluginWebRequest request, CancellationToken cancellationToken);
}

/// <summary>Supported host-mediated HTTPS request methods.</summary>
public enum PluginWebRequestMethod
{
    /// <summary>Reads a bounded response without a request body.</summary>
    Get,
    /// <summary>Sends a bounded request body and reads the response.</summary>
    Post
}

/// <summary>Immutable request value accepted by <see cref="IPluginNetworkService"/>.</summary>
public sealed class PluginWebRequest
{
    /// <summary>Creates a request with an optional UTF-8 body.</summary>
    public PluginWebRequest(PluginWebRequestMethod method, Uri uri, string? content = null, string? contentType = null)
    {
        if (!Enum.IsDefined(typeof(PluginWebRequestMethod), method))
        {
            throw new ArgumentOutOfRangeException(nameof(method));
        }

        if (uri == null || !uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Plugin web requests require an absolute HTTPS URI.", nameof(uri));
        }

        if (method == PluginWebRequestMethod.Get && !string.IsNullOrEmpty(content))
        {
            throw new ArgumentException("GET requests cannot include a body.", nameof(content));
        }

        Method = method;
        Uri = uri;
        Content = content ?? string.Empty;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType!;
    }

    /// <summary>Requested HTTP method.</summary>
    public PluginWebRequestMethod Method { get; }

    /// <summary>Validated HTTPS destination.</summary>
    public Uri Uri { get; }

    /// <summary>UTF-8 request body for POST requests.</summary>
    public string Content { get; }

    /// <summary>Media type for POST content.</summary>
    public string ContentType { get; }
}

/// <summary>Detached bounded response returned by the host transport.</summary>
public sealed class PluginWebResponse
{
    /// <summary>Creates an immutable response.</summary>
    public PluginWebResponse(int statusCode, string content, Uri? redirectLocation = null)
    {
        StatusCode = statusCode;
        Content = content ?? string.Empty;
        RedirectLocation = redirectLocation;
    }

    /// <summary>HTTP status code returned by the remote endpoint.</summary>
    public int StatusCode { get; }

    /// <summary>Bounded response body decoded as UTF-8.</summary>
    public string Content { get; }

    /// <summary>Optional redirect destination reported by the host transport. The host validates
    /// every redirect before issuing the next request; plugins should not follow this value.</summary>
    public Uri? RedirectLocation { get; }

    /// <summary>Whether the response represents a successful HTTP status code.</summary>
    public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode <= 299;
}
