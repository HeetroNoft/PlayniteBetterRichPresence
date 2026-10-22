using Playnite.SDK;
using System;
using System.Windows;

namespace BetterRichPresence.Localization
{
    /// <summary>
    /// Translations live in Localization/&lt;language&gt;.xaml, compiled into the DLL. Strings are
    /// merged into the application resources, so XAML uses them with DynamicResource and code with <see cref="Get"/>.
    /// </summary>
    internal static class Loc
    {
        private const string BaseLanguage = "en_US";
        private static readonly ILogger logger = LogManager.GetLogger();

        /// <param name="language">Playnite's language, e.g. "fr_FR"; its default ("english") keeps English.</param>
        public static void Load(string language)
        {
            // English first, then the user's language on top: a partial translation falls back to English.
            Merge(BaseLanguage);
            if (!string.IsNullOrEmpty(language) && language != BaseLanguage && language.Contains("_"))
            {
                Merge(language);
            }
        }

        public static string Get(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        private static void Merge(string language)
        {
            try
            {
                var uri = new Uri($"/BetterRichPresence;component/Localization/{language}.xaml", UriKind.Relative);
                Application.Current?.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
            catch (Exception e)
            {
                // No translation for this language yet: English stays.
                logger.Debug($"No {language} translation: {e.Message}");
            }
        }
    }
}
