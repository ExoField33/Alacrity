using System;
using System.Diagnostics;
using System.Windows.Forms;
using Alacrity.Core;

namespace AlacrityTerraria
{
    /// <summary>Terraria runtime implementation for host-mediated clipboard and browser operations.</summary>
    internal sealed class TerrariaPluginUserInteractionBackend : IPluginUserInteractionBackend
    {
        public bool TryReadClipboard(out string text)
        {
            text = string.Empty;
            try
            {
                if (!Clipboard.ContainsText()) return false;
                text = Clipboard.GetText() ?? string.Empty;
                return true;
            }
            catch { return false; }
        }

        public bool TryWriteClipboard(string text)
        {
            try { Clipboard.SetText(text ?? string.Empty); return true; }
            catch { return false; }
        }

        public bool TryOpenExternalLink(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return false;
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }
    }
}
