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
        return CreateService(manifest, null);
    }

    public IPluginUserInteractionService CreateService(PluginManifest manifest, IPluginResourceScope? resources)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (!manifest.Id.IsValid) throw new ArgumentException("User-interaction services require a valid plugin owner.", nameof(manifest));
        var guard = resources == null ? null : new ScopeGuard();
        if (guard != null)
        {
            try { resources!.Own("user-interaction", PluginResourceKind.UserInterface, guard); }
            catch { guard.Dispose(); throw; }
        }
        return new ScopedService(manifest, backend, guard);
    }

    private sealed class ScopedService : IPluginUserInteractionService
    {
        private readonly PluginManifest manifest;
        private readonly IPluginUserInteractionBackend backend;
        private readonly ScopeGuard? guard;

        public ScopedService(PluginManifest manifest, IPluginUserInteractionBackend backend, ScopeGuard? guard)
        {
            this.manifest = manifest;
            this.backend = backend;
            this.guard = guard;
        }

        public bool TryReadClipboard(out string text)
        {
            EnsureActive();
            text = string.Empty;
            if (!Allows(PluginPermission.Clipboard)) return false;
            try { return backend.TryReadClipboard(out text); }
            catch { text = string.Empty; return false; }
        }

        public bool TryWriteClipboard(string text)
        {
            EnsureActive();
            return Allows(PluginPermission.Clipboard) && Try(() => backend.TryWriteClipboard(text ?? string.Empty));
        }

        public bool TryOpenExternalLink(Uri uri)
        {
            EnsureActive();
            return uri != null && IsApprovedLink(uri) && Allows(PluginPermission.OpenExternalLinks) && Try(() => backend.TryOpenExternalLink(uri));
        }

        private bool Allows(PluginPermission permission) => manifest.Permissions.HasFlag(permission);
        private void EnsureActive()
        {
            if (guard != null && guard.IsReleased) throw new ObjectDisposedException("IPluginUserInteractionService", "The owning plugin scope has been released.");
        }
        private static bool IsApprovedLink(Uri uri) => uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        private static bool Try(Func<bool> operation) { try { return operation(); } catch { return false; } }
    }

    private sealed class ScopeGuard : IDisposable { private int released; internal bool IsReleased => System.Threading.Volatile.Read(ref released) != 0; public void Dispose() { System.Threading.Interlocked.Exchange(ref released, 1); } }
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
