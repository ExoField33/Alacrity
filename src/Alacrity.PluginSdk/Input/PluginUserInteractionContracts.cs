using System;

namespace Alacrity.PluginSdk;

/// <summary>
/// Provides explicitly host-mediated user interactions. Implementations may reject operations when
/// the verified package lacks the required permission or the platform service is unavailable.
/// </summary>
public interface IPluginUserInteractionService
{
    /// <summary>Attempts to read user clipboard text for a user-initiated operation.</summary>
    bool TryReadClipboard(out string text);

    /// <summary>Attempts to write user clipboard text for a user-initiated operation.</summary>
    bool TryWriteClipboard(string text);

    /// <summary>Attempts to open a validated HTTP or HTTPS URI after explicit user interaction.</summary>
    bool TryOpenExternalLink(Uri uri);
}
