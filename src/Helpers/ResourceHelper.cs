using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kronos.Language;

#if WINDOWS
using Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.Resources.Core;
#endif

namespace Kronos.Helpers;

public class ResourceHelper
{
    private const string error = "LangResourceError";

#if WINDOWS
    static readonly ResourceLoader _resourceLoader = new ResourceLoader();
    static readonly ResourceContext _resourceContext = ResourceContext.GetForViewIndependentUse();
    static readonly ResourceMap _resourceMap = ResourceManager.Current.MainResourceMap.GetSubtree("Resources");
#endif

    static readonly Dictionary<string, string> _resources = new Dictionary<string, string>();

    static bool _translatorModeEnabled;
#if DEBUG
    static bool _langHunterEnabled;
#endif

    internal static bool TranslatorModeEnabled
    {
        get { return _translatorModeEnabled; }
        set
        {
            if (value != _translatorModeEnabled)
            {
                _translatorModeEnabled = value;
                LanguageManager.Instance.ReloadLanguage();
            }
        }
    }


    internal static void LoadResource(string key)
    {
#if DEBUG
        if (key == "LANG_HUNT")
        {
            _langHunterEnabled = true;
        }
        else
        {
            _langHunterEnabled = false;
        }
#endif
        _resources.Clear();
#if WINDOWS
        try
        {
            _resourceContext.Languages = new List<string> { key };
        }
        catch (Exception err)
        {
            Logger.Error($"Could not load resource map for key {key}: {err.Message}");
        }
#else
        _languageTag = key;
        _stringsByLanguage = null;
#endif
    }

    internal static void UpdateFromLiveTranslations(List<TranslationRow> translations)
    {
        _resources.Clear();
        foreach (var translation in translations)
        {
            if (string.IsNullOrWhiteSpace(translation.NewTranslation))
            {
                continue;
            }
            // Add the translation to our dictionary.
            _resources[translation.Key] = translation.NewTranslation;
        }
        LanguageManager.Instance.ReloadLanguage();
    }

    public static string GetString(string resourceName)
    {

#if DEBUG
        if (_langHunterEnabled)
        {
            return "...";
        }
#endif

        // Load from our dictionary if we are in translator mode, but then fallback if we don't have the value.
        if (TranslatorModeEnabled && _resources.TryGetValue(resourceName, out var value))
        {
            return value;
        }

#if WINDOWS
        // But if we have a resource map fall back to it.
        var resourceCandidate = _resourceMap.GetValue(resourceName, _resourceContext);
        if (string.IsNullOrWhiteSpace(resourceCandidate?.ValueAsString) == false)
        {
            return resourceCandidate.ValueAsString;
        }

        Debug.WriteLine($"Translation not found: {resourceName}");

        // If not we fallback to the original language.
        var fallbackString = _resourceLoader.GetString(resourceName);

#if DEBUG
        if (string.IsNullOrWhiteSpace(fallbackString))
        {
            Debug.WriteLine($"Translation not found: {resourceName}");
            Debugger.Break();
        }
#endif
        return fallbackString;
#else
        return GetStringFromResw(resourceName);
#endif
    }

#if !WINDOWS
    /// <summary>The requested language, e.g. "en-US". Defaults to en-US.</summary>
    static string _languageTag = "en-US";

    /// <summary>Parsed strings for <see cref="_languageTag"/>, built on first use.</summary>
    static Dictionary<string, string>? _stringsByLanguage;

    /// <summary>Strings for en-US, used whenever the requested language has no .resw.</summary>
    static Dictionary<string, string>? _fallbackStrings;

    /// <summary>
    /// Reads a string out of the translated .resw file for the current language.
    /// </summary>
    /// <remarks>
    /// On Windows these strings come from WinUI's ResourceManager, which compiles the .resw files
    /// into a PRI. There is no equivalent here, so the .resw is read as the XML it already is -
    /// which is exactly the same data WinUI would have baked into the PRI. That keeps the Linux CLI
    /// genuinely localised rather than printing resource keys.
    ///
    /// Returns the key itself when a string is missing. A missing translation is a missing label,
    /// not a reason to throw out of whatever operation happened to want the label.
    /// </remarks>
    static string GetStringFromResw(string resourceName)
    {
        var strings = _stringsByLanguage ??= LoadResw(_languageTag);

        if (strings.TryGetValue(resourceName, out var found) && string.IsNullOrWhiteSpace(found) == false)
        {
            return found;
        }

        // An unknown or unsupported language falls back to en-US rather than to the key.
        var fallback = _fallbackStrings ??= LoadResw("en-US");
        if (fallback.TryGetValue(resourceName, out var fallbackValue) && string.IsNullOrWhiteSpace(fallbackValue) == false)
        {
            return fallbackValue;
        }

        Logger.Info($"Translation not found: {resourceName}");
        return resourceName;
    }

    /// <summary>
    /// Parses Translations/&lt;language&gt;/Resources.resw into a key/value map.
    /// </summary>
    /// <remarks>
    /// A missing directory yields an empty map rather than throwing: the CLI has to start and report
    /// what it can even when it was published without its translations.
    /// </remarks>
    static Dictionary<string, string> LoadResw(string language)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(language))
        {
            return strings;
        }

        var reswPath = Path.Combine(AppContext.BaseDirectory, "Translations", language, "Resources.resw");
        if (File.Exists(reswPath) == false)
        {
            return strings;
        }

        try
        {
            var document = XDocument.Load(reswPath);

            foreach (var data in document.Descendants("data"))
            {
                var name = data.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Value is the element's text, which is what ReswString serialises. A comment child
                // is documentation and is skipped by reading Value rather than InnerText.
                var text = data.Element("value")?.Value;
                if (string.IsNullOrWhiteSpace(text) == false)
                {
                    strings[name] = text!;
                }
            }
        }
        catch (Exception err) when (err is System.Xml.XmlException or IOException)
        {
            Logger.Error(err, $"Could not parse {reswPath}.");
        }

        return strings;
    }
#endif

    public static string GetFormattedResourceTemplate(string templateResourceName, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, GetString(templateResourceName), args);
        }
        catch
        {
            return error;
        }
    }
}
