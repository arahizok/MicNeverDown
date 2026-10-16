using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MicNeverDown
{
    public enum AppLanguage { English, Russian }

    public sealed class UiPreferences
    {
        public string Language;
    }

    // UI preference only: the microphone configuration remains unchanged.
    public static class Ui
    {
        public static readonly string PreferencesPath = Path.Combine(Storage.DirectoryPath, "ui.xml");
        public static AppLanguage CurrentLanguage { get; private set; }
        public static CultureInfo Culture { get { return CultureInfo.GetCultureInfo(CurrentLanguage == AppLanguage.Russian ? "ru-RU" : "en-US"); } }
        public static event EventHandler LanguageChanged;

        public static string Text(string english, string russian)
        {
            return CurrentLanguage == AppLanguage.Russian ? russian : english;
        }

        public static void Initialize()
        {
            AppLanguage language = AppLanguage.English;
            try
            {
                UiPreferences preferences = Storage.Read<UiPreferences>(PreferencesPath);
                if (preferences != null && preferences.Language == "ru") language = AppLanguage.Russian;
                else if (preferences == null || preferences.Language != "en") throw new InvalidDataException("Unknown UI language. Using English.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception error) { Storage.WriteError(error); }
            Apply(language);
        }

        public static void SetLanguage(AppLanguage language)
        {
            if (language != AppLanguage.English && language != AppLanguage.Russian) throw new ArgumentOutOfRangeException("language");
            if (language == CurrentLanguage) return;
            // Publish the new UI language only after its preference is saved.
            Storage.Write(PreferencesPath, new UiPreferences { Language = language == AppLanguage.Russian ? "ru" : "en" });
            Apply(language);
        }

        private static void Apply(AppLanguage language)
        {
            CurrentLanguage = language;
            Thread.CurrentThread.CurrentCulture = Culture;
            Thread.CurrentThread.CurrentUICulture = Culture;
            if (LanguageChanged != null) LanguageChanged(null, EventArgs.Empty);
        }
    }
}
