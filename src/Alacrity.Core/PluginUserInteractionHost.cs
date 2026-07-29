using System;
using Alacrity.PluginSdk;

namespace Alacrity.Core;

/// <summary>Platform implementation retained by the host; plugins receive only the scoped public service.</summary>
public interface IPluginUserInteractionBackend
{
    bool TryReadClipboard(out string text);
    bool TryWriteClipboard(string text);
    bool TryOpenExternalLink(Uri uri);
}

/// <summary>Creates permission-gated user-interaction services from verified package manifests.</summary>
public sealed class PluginUserInteractionHost
{
    private readonly IPluginUserInteractionBackend backend;

    public PluginUserInteractionHost(IPluginUserInteractionBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public IPluginUserInteractionService CreateService(PluginManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (!manifest.Id.IsValid) throw new ArgumentException("User-interaction services require a valid plugin owner.", nameof(manifest));
        return new ScopedService(manifest, backend);
    }

    private sealed class ScopedService : IPluginUserInteractionService
    {
        private readonly PluginManifest manifest;
        private readonly IPluginUserInteractionBackend backend;

        public ScopedService(PluginManifest manifest, IPluginUserInteractionBackend backend)
        {
            this.manifest = manifest;
            this.backend = backend;
        }

        public bool TryReadClipboard(out string text)
        {
            text = string.Empty;
            if (!Allows(PluginPermission.Clipboard)) return false;
            try { return backend.TryReadClipboard(out text); }
            catch { text = string.Empty; return false; }
        }

        public bool TryWriteClipboard(string text)
        {
            return Allows(PluginPermission.Clipboard) && Try(() => backend.TryWriteClipboard(text ?? string.Empty));
        }

        public bool TryOpenExternalLink(Uri uri)
        {
            return uri != null && IsApprovedLink(uri) && Allows(PluginPermission.OpenExternalLinks) && Try(() => backend.TryOpenExternalLink(uri));
        }

        private bool Allows(PluginPermission permission) => manifest.Permissions.HasFlag(permission);
        private static bool IsApprovedLink(Uri uri) => uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        private static bool Try(Func<bool> operation) { try { return operation(); } catch { return false; } }
    }
}

/// <summary>Safe no-op backend used when a runtime does not expose platform interactions.</summary>
public sealed class UnsupportedPluginUserInteractionBackend : IPluginUserInteractionBackend
{
    public static readonly UnsupportedPluginUserInteractionBackend Instance = new UnsupportedPluginUserInteractionBackend();
    private UnsupportedPluginUserInteractionBackend() { }
    public bool TryReadClipboard(out string text) { text = string.Empty; return false; }
    public bool TryWriteClipboard(string text) => false;
    public bool TryOpenExternalLink(Uri uri) => false;
}
