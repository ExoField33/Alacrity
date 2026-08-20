using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alacrity.PluginSdk;

namespace Alacrity.ChatTranslation;

/// <summary>Activation-local client for the Google Translate-compatible endpoint. It owns only
/// detached response caching; authorization, HTTPS transport, cancellation, and diagnostics stay
/// in the generic host services passed at construction.</summary>
internal sealed class GoogleTranslationClient
{
    private const int MaximumCachedTranslations = 128;
    // This key belongs to the Alacrity Chat Translation plugin. Restrict it to the
    // translate-pa endpoint in its Google Cloud project before publishing builds.
    private const string BuiltInApiKey = "AIzaSyDLEeFI5OtFBwYBIoK_jj5m32rZK5CkCXA";
    private readonly object gate = new object();
    private readonly IPluginNetworkService network;
    private readonly IPluginLogger logger;
    private readonly Dictionary<TranslationKey, TranslationResult> cache = new Dictionary<TranslationKey, TranslationResult>();
    private readonly Queue<TranslationKey> cacheOrder = new Queue<TranslationKey>();

    internal GoogleTranslationClient(IPluginNetworkService network, IPluginLogger logger)
    {
        this.network = network ?? throw new ArgumentNullException(nameof(network));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async Task<TranslationResult?> TranslateAsync(string text, string source, string target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4500)
        {
            return null;
        }

        var key = new TranslationKey(text, source, target);
        lock (gate)
        {
            if (cache.TryGetValue(key, out TranslationResult cached))
            {
                return cached;
            }
        }

        try
        {
            var request = new PluginWebRequest(PluginWebRequestMethod.Get, BuildTranslatePaUri(text, source, target));
            PluginWebResponse response = await network.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.Error("Google translation returned HTTP " + response.StatusCode + ".", null);
                return null;
            }

            TranslationResult? result = ParseTranslatePaResponse(response.Content, source);
            if (result == null)
            {
                return null;
            }

            lock (gate)
            {
                cache[key] = result;
                cacheOrder.Enqueue(key);
                while (cacheOrder.Count > MaximumCachedTranslations)
                {
                    cache.Remove(cacheOrder.Dequeue());
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Error("Google translation request failed.", exception);
            return null;
        }
    }

    internal void Clear()
    {
        lock (gate)
        {
            cache.Clear();
            cacheOrder.Clear();
        }
    }

    private static Uri BuildTranslatePaUri(string text, string source, string target)
    {
        var builder = new StringBuilder(text.Length + source.Length + target.Length + BuiltInApiKey.Length + 180);
        builder.Append("https://translate-pa.googleapis.com/v1/translate?params.client=gtx&dataTypes=TRANSLATION&key=")
            .Append(Uri.EscapeDataString(BuiltInApiKey))
            .Append("&query.sourceLanguage=")
            .Append(Uri.EscapeDataString(source))
            .Append("&query.targetLanguage=")
            .Append(Uri.EscapeDataString(target))
            .Append("&query.text=")
            .Append(Uri.EscapeDataString(text));
        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static TranslationResult? ParseTranslatePaResponse(string json, string requestedSource)
    {
        try
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
            {
                var serializer = new DataContractJsonSerializer(typeof(TranslatePaEnvelope));
                var envelope = (TranslatePaEnvelope?)serializer.ReadObject(stream);
                if (envelope == null)
                {
                    return null;
                }

                TranslatePaTranslation[]? translations = envelope.Translations;
                if ((translations == null || translations.Length == 0) && envelope.Data != null)
                {
                    translations = envelope.Data.Translations;
                }

                if (translations != null && translations.Length != 0)
                {
                    TranslatePaTranslation translation = translations[0];
                    if (!string.IsNullOrWhiteSpace(translation.TranslatedText))
                    {
                        string language = string.IsNullOrWhiteSpace(translation.DetectedLanguageCode)
                            ? requestedSource
                            : translation.DetectedLanguageCode!;
                        return new TranslationResult(translation.TranslatedText!, language);
                    }
                }

                // Retain the old flat form for deterministic fake-host fixtures and endpoint
                // compatibility, while production uses the documented translations array.
                if (string.IsNullOrWhiteSpace(envelope.Translation))
                {
                    return null;
                }

                string flatLanguage = string.IsNullOrWhiteSpace(envelope.SourceLanguage) ? requestedSource : envelope.SourceLanguage!;
                return new TranslationResult(envelope.Translation!, flatLanguage);
            }
        }
        catch (SerializationException)
        {
            return null;
        }
    }

    private readonly struct TranslationKey : IEquatable<TranslationKey>
    {
        internal TranslationKey(string text, string source, string target)
        {
            Text = text;
            Source = source;
            Target = target;
        }

        private string Text { get; }
        private string Source { get; }
        private string Target { get; }

        public bool Equals(TranslationKey other)
        {
            return string.Equals(Text, other.Text, StringComparison.Ordinal) &&
                string.Equals(Source, other.Source, StringComparison.Ordinal) &&
                string.Equals(Target, other.Target, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is TranslationKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Text == null ? 0 : Text.GetHashCode();
                hash = (hash * 397) ^ (Source == null ? 0 : Source.GetHashCode());
                return (hash * 397) ^ (Target == null ? 0 : Target.GetHashCode());
            }
        }
    }

    internal sealed class TranslationResult
    {
        internal TranslationResult(string text, string sourceLanguage)
        {
            Text = text ?? string.Empty;
            SourceLanguage = sourceLanguage ?? string.Empty;
        }

        internal string Text { get; }
        internal string SourceLanguage { get; }
    }

    [DataContract]
        private sealed class TranslatePaEnvelope
        {
            [DataMember(Name = "translations")]
            public TranslatePaTranslation[]? Translations { get; set; }

            [DataMember(Name = "data")]
            public TranslatePaData? Data { get; set; }

        [DataMember(Name = "sourceLanguage")]
        public string? SourceLanguage { get; set; }

        [DataMember(Name = "translation")]
            public string? Translation { get; set; }
        }

        [DataContract]
        private sealed class TranslatePaTranslation
        {
            [DataMember(Name = "translatedText")]
            public string? TranslatedText { get; set; }

            [DataMember(Name = "detectedLanguageCode")]
            public string? DetectedLanguageCode { get; set; }
        }

        [DataContract]
        private sealed class TranslatePaData
        {
            [DataMember(Name = "translations")]
            public TranslatePaTranslation[]? Translations { get; set; }
        }

}
