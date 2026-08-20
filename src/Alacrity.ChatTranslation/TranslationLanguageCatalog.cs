using System;
using Alacrity.PluginSdk;

namespace Alacrity.ChatTranslation;

/// <summary>Immutable language choices for the official Google Translation Basic endpoint. Keeping
/// this list local means opening a language chooser never competes with a chat translation task.</summary>
internal static class TranslationLanguageCatalog
{
    internal static readonly PluginSettingOption[] DefaultTargets = CreateTargets();
    internal static readonly PluginSettingOption[] DefaultSources = CreateSources(DefaultTargets);

    internal static PluginSettingOption[] CreateSources(PluginSettingOption[] targets)
    {
        var sources = new PluginSettingOption[targets.Length + 1];
        sources[0] = new PluginSettingOption("auto", "Auto detect");
        Array.Copy(targets, 0, sources, 1, targets.Length);
        return sources;
    }

    private static PluginSettingOption[] CreateTargets()
    {
        return new[]
        {
            Option("af", "Afrikaans"), Option("sq", "Albanian"), Option("am", "Amharic"), Option("ar", "Arabic"), Option("hy", "Armenian"),
            Option("as", "Assamese"), Option("ay", "Aymara"), Option("az", "Azerbaijani"), Option("bm", "Bambara"), Option("eu", "Basque"),
            Option("be", "Belarusian"), Option("bn", "Bengali"), Option("bho", "Bhojpuri"), Option("bs", "Bosnian"), Option("bg", "Bulgarian"),
            Option("ca", "Catalan"), Option("ceb", "Cebuano"), Option("ny", "Chichewa"), Option("zh", "Chinese (Simplified)"), Option("zh-TW", "Chinese (Traditional)"),
            Option("co", "Corsican"), Option("hr", "Croatian"), Option("cs", "Czech"), Option("da", "Danish"), Option("dv", "Divehi"),
            Option("doi", "Dogri"), Option("nl", "Dutch"), Option("en", "English"), Option("eo", "Esperanto"), Option("et", "Estonian"),
            Option("ee", "Ewe"), Option("fil", "Filipino"), Option("fi", "Finnish"), Option("fr", "French"), Option("fy", "Frisian"),
            Option("gl", "Galician"), Option("ka", "Georgian"), Option("de", "German"), Option("el", "Greek"), Option("gn", "Guarani"),
            Option("gu", "Gujarati"), Option("ht", "Haitian Creole"), Option("ha", "Hausa"), Option("haw", "Hawaiian"), Option("he", "Hebrew"),
            Option("hi", "Hindi"), Option("hmn", "Hmong"), Option("hu", "Hungarian"), Option("is", "Icelandic"), Option("ig", "Igbo"),
            Option("ilo", "Ilocano"), Option("id", "Indonesian"), Option("ga", "Irish"), Option("it", "Italian"), Option("ja", "Japanese"),
            Option("jv", "Javanese"), Option("kn", "Kannada"), Option("kk", "Kazakh"), Option("km", "Khmer"), Option("rw", "Kinyarwanda"),
            Option("gom", "Konkani"), Option("ko", "Korean"), Option("kri", "Krio"), Option("ku", "Kurdish (Kurmanji)"), Option("ckb", "Kurdish (Sorani)"),
            Option("ky", "Kyrgyz"), Option("lo", "Lao"), Option("la", "Latin"), Option("lv", "Latvian"), Option("ln", "Lingala"),
            Option("lt", "Lithuanian"), Option("lg", "Luganda"), Option("lb", "Luxembourgish"), Option("mk", "Macedonian"), Option("mai", "Maithili"),
            Option("mg", "Malagasy"), Option("ms", "Malay"), Option("ml", "Malayalam"), Option("mt", "Maltese"), Option("mi", "Maori"),
            Option("mr", "Marathi"), Option("mni-Mtei", "Meiteilon (Manipuri)"), Option("min", "Minang"), Option("mn", "Mongolian"), Option("my", "Myanmar (Burmese)"),
            Option("ne", "Nepali"), Option("no", "Norwegian"), Option("or", "Odia"), Option("om", "Oromo"), Option("ps", "Pashto"),
            Option("fa", "Persian"), Option("pl", "Polish"), Option("pt", "Portuguese"), Option("pa", "Punjabi"), Option("qu", "Quechua"),
            Option("ro", "Romanian"), Option("ru", "Russian"), Option("sm", "Samoan"), Option("sa", "Sanskrit"), Option("gd", "Scots Gaelic"),
            Option("nso", "Sepedi"), Option("sr", "Serbian"), Option("st", "Sesotho"), Option("sn", "Shona"), Option("sd", "Sindhi"),
            Option("si", "Sinhala"), Option("sk", "Slovak"), Option("sl", "Slovenian"), Option("so", "Somali"), Option("es", "Spanish"),
            Option("su", "Sundanese"), Option("sw", "Swahili"), Option("sv", "Swedish"), Option("tg", "Tajik"), Option("ta", "Tamil"),
            Option("tt", "Tatar"), Option("te", "Telugu"), Option("th", "Thai"), Option("ti", "Tigrinya"), Option("ts", "Tsonga"),
            Option("tr", "Turkish"), Option("tk", "Turkmen"), Option("ak", "Twi"), Option("uk", "Ukrainian"), Option("ur", "Urdu"),
            Option("ug", "Uyghur"), Option("uz", "Uzbek"), Option("vi", "Vietnamese"), Option("cy", "Welsh"), Option("xh", "Xhosa"),
            Option("yi", "Yiddish"), Option("yo", "Yoruba"), Option("zu", "Zulu")
        };
    }

    private static PluginSettingOption Option(string value, string displayName)
    {
        return new PluginSettingOption(value, displayName);
    }
}
