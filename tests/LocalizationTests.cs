using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Forms;
using MicNeverDown;

internal static class LocalizationTests
{
    private static int passed;
    private static string PreferencesPath { get { return Path.Combine(Storage.DirectoryPath, "ui.xml"); } }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
        Console.WriteLine("PASS: " + message);
    }
    private static void ExpectLanguage(AppLanguage language)
    {
        Check(Ui.CurrentLanguage == language, "Current language is " + language);
        Check(Ui.Culture.Name == (language == AppLanguage.English ? "en-US" : "ru-RU"), "UI culture matches language");
        Check(Ui.Text("English marker", "Русская метка") == (language == AppLanguage.English ? "English marker" : "Русская метка"), "Translated text follows active language");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string mode = args[0];
            // The runner gives every scenario its own executable directory.
            // Never point this harness at the installed application's folder.
            Check(!File.Exists(PreferencesPath), "Scenario begins with an isolated preference file");
            if (mode == "transitions") TestTransitions();
            else if (mode == "write-failure") TestWriteFailure();
            else if (mode == "legacy-settings") TestLegacySettings();
            else if (mode == "tray") TestTray();
            else if (mode == "ui") TestUi();
            else TestInitialization(mode);
            Console.WriteLine("{0}: {1} checks passed.", mode, passed);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
    private static void WriteFixture(string xml)
    {
        Directory.CreateDirectory(Storage.DirectoryPath);
        File.WriteAllText(PreferencesPath, xml, new UTF8Encoding(false));
    }
    private static void TestInitialization(string mode)
    {
        if (mode == "en" || mode == "ru" || mode == "invalid" || mode == "empty")
            Storage.Write(PreferencesPath, new UiPreferences { Language = mode == "invalid" ? "de" : mode == "empty" ? "" : mode });
        else if (mode == "malformed") WriteFixture("<UiPreferences><Language>ru");
        else if (mode == "oversized") WriteFixture(new string(' ', 65537));
        else if (mode == "dtd") WriteFixture("<!DOCTYPE UiPreferences [<!ENTITY language 'ru'>]><UiPreferences><Language>&language;</Language></UiPreferences>");
        else if (mode != "missing") throw new ArgumentException("Unknown scenario: " + mode);
        byte[] previous = File.Exists(PreferencesPath) ? File.ReadAllBytes(PreferencesPath) : null;
        Ui.Initialize();
        ExpectLanguage(mode == "ru" ? AppLanguage.Russian : AppLanguage.English);
        if (previous == null) Check(!File.Exists(PreferencesPath), "Reading a missing preference does not create a file");
        else Check(Convert.ToBase64String(previous) == Convert.ToBase64String(File.ReadAllBytes(PreferencesPath)), "Initialization does not overwrite existing preference data");
    }
    private static void TestTransitions()
    {
        Ui.Initialize();
        ExpectLanguage(AppLanguage.English);
        int changes = 0;
        EventHandler changed = delegate
        {
            changes++;
            string expected = Ui.CurrentLanguage == AppLanguage.Russian ? "ru" : "en";
            Check(Storage.Read<UiPreferences>(PreferencesPath).Language == expected, "Preference is durable before change notification");
        };
        Ui.LanguageChanged += changed;
        try
        {
            Ui.SetLanguage(AppLanguage.Russian);
            ExpectLanguage(AppLanguage.Russian);
            Check(changes == 1, "First switch raises one notification");
            Check(Storage.Read<UiPreferences>(PreferencesPath).Language == "ru", "Russian choice persists as ru");
            Ui.SetLanguage(AppLanguage.Russian);
            Check(changes == 1, "Choosing the active language does not notify again");
            Ui.SetLanguage(AppLanguage.English);
            ExpectLanguage(AppLanguage.English);
            Check(changes == 2 && Storage.Read<UiPreferences>(PreferencesPath).Language == "en", "English choice persists and notifies");
            bool rejected = false;
            try { Ui.SetLanguage((AppLanguage)987); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Unsupported enum value is rejected");
            Check(changes == 2 && Ui.CurrentLanguage == AppLanguage.English, "Rejected language leaves current state unchanged");
            Check(Directory.GetFiles(Storage.DirectoryPath, ".ui.xml.*.tmp").Length == 0, "Successful switches leave no temporary files");
        }
        finally { Ui.LanguageChanged -= changed; }
    }
    private static void TestWriteFailure()
    {
        Ui.Initialize();
        Directory.CreateDirectory(PreferencesPath);
        int changes = 0;
        EventHandler changed = delegate { changes++; };
        Ui.LanguageChanged += changed;
        bool failed = false;
        try
        {
            try { Ui.SetLanguage(AppLanguage.Russian); }
            catch (IOException) { failed = true; }
            catch (UnauthorizedAccessException) { failed = true; }
            Check(failed, "Blocked preference destination reports a save failure");
            ExpectLanguage(AppLanguage.English);
            Check(changes == 0, "Failed persistence does not notify listeners");
            Check(Directory.Exists(PreferencesPath), "Failed save preserves the blocking destination");
            Check(Directory.GetFiles(Storage.DirectoryPath, ".ui.xml.*.tmp").Length == 0, "Failed preference save leaves no temporary files");
        }
        finally { Ui.LanguageChanged -= changed; }
    }
    private static void TestLegacySettings()
    {
        Directory.CreateDirectory(Storage.DirectoryPath);
        string xml = "<?xml version=\"1.0\"?><Settings><DeviceId>test-microphone</DeviceId><DeviceName>Legacy headset</DeviceName><Level>0.837</Level></Settings>";
        File.WriteAllText(Storage.SettingsPath, xml, new UTF8Encoding(false));
        Ui.Initialize();
        Ui.SetLanguage(AppLanguage.Russian);
        Ui.SetLanguage(AppLanguage.English);
        Check(File.ReadAllText(Storage.SettingsPath) == xml, "Language changes leave legacy microphone settings byte-for-byte unchanged");
        Settings loaded = Storage.Read<Settings>(Storage.SettingsPath);
        Storage.Validate(loaded);
        Check(loaded.DeviceId == "test-microphone" && Math.Abs(loaded.Level - 0.837f) < 0.00001f, "Legacy device and exact target level still load");
        Check(typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 3, "Legacy microphone settings schema retains exactly three fields");
    }
    private static T Find<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T) return (T)control;
            T child = Find<T>(control);
            if (child != null) return child;
        }
        return null;
    }
    private static void TestTray()
    {
        Ui.Initialize();
        // Bypass the application constructor so this fixture cannot register
        // real instance events, create a visible tray icon, or open real audio.
        TrayApp context = (TrayApp)FormatterServices.GetUninitializedObject(typeof(TrayApp));
        Dictionary<string, ToolStripMenuItem> items = new Dictionary<string, ToolStripMenuItem>();
        string[] fields = { "settingsItem", "pauseItem", "startupItem", "languageItem", "englishItem", "russianItem", "disableStartupExitItem", "exitItem" };
        foreach (string name in fields)
        {
            ToolStripMenuItem item = new ToolStripMenuItem();
            items.Add(name, item);
            typeof(TrayApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, item);
        }
        using (GuardEngine engine = new GuardEngine(new NoAudioProvider()))
        using (NotifyIcon icon = new NotifyIcon { Visible = false })
        {
            typeof(TrayApp).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, engine);
            typeof(TrayApp).GetField("tray", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, icon);
            MethodInfo apply = typeof(TrayApp).GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance);
            apply.Invoke(context, null);
            Check(items["settingsItem"].Text == "Microphone and volume…" && items["languageItem"].Text == "Language" && items["exitItem"].Text == "Exit", "Tray command labels render in English");
            Check(items["englishItem"].Checked && !items["russianItem"].Checked, "English is the only checked tray language");
            Check(icon.Text == "MicNeverDown: select a microphone", "Unconfigured tray tooltip renders in English");
            engine.Configure(new Settings { DeviceId = "fixture-only", DeviceName = "Fixture", Level = 0.5f });
            engine.Paused = true;
            Ui.SetLanguage(AppLanguage.Russian);
            apply.Invoke(context, null);
            Check(items["languageItem"].Text == "Язык" && items["startupItem"].Text == "Запускать вместе с Windows" && items["disableStartupExitItem"].Text == "Отключить автозапуск и выйти", "Tray command labels render in Russian");
            Check(!items["englishItem"].Checked && items["russianItem"].Checked, "Russian is the only checked tray language");
            Check(items["pauseItem"].Text == "Возобновить" && icon.Text == "MicNeverDown: пауза", "Russian resume command and paused tooltip match guard state");
            Ui.SetLanguage(AppLanguage.English);
            apply.Invoke(context, null);
            Check(items["pauseItem"].Text == "Resume" && icon.Text == "MicNeverDown: paused", "Paused tray state switches back to English");
        }
        foreach (ToolStripMenuItem item in items.Values) item.Dispose();
        Check(!File.Exists(Storage.SettingsPath), "Tray localization fixture does not change microphone configuration");
    }
    private sealed class NoAudioProvider : IAudioProvider
    {
        public List<AudioDeviceInfo> ListCapture() { throw new Exception("Tray fixture must not enumerate capture devices."); }
        public IVolumeEndpoint Open(string id) { throw new Exception("Tray fixture must not open an endpoint."); }
    }
    private static Control FindByTypeName(Control root, string name)
    {
        foreach (Control control in root.Controls)
        {
            if (control.GetType().Name == name) return control;
            Control child = FindByTypeName(control, name);
            if (child != null) return child;
        }
        return null;
    }
    private static void SaveRendering(Form form, string name)
    {
        using (Bitmap image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), ImageFormat.Png);
        }
    }
    private static void TestUi()
    {
        Ui.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        List<AudioDeviceInfo> available = AudioDevices.ListCapture(); // Read-only.
        Check(available.Count > 0, "A capture device is available for the read-only UI fixture");
        Settings initial = new Settings { DeviceId = available[0].Id, DeviceName = available[0].Name, Level = 0.837f };
        Settings captured = null;
        bool capturedStartup = false;
        int saves = 0;
        using (SettingsForm form = new SettingsForm(initial, delegate(Settings settings, bool enabled)
        {
            captured = settings;
            capturedStartup = enabled;
            saves++;
        }))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-30000, -30000);
            form.Show();
            Application.DoEvents();
            NumericUpDown numeric = Find<NumericUpDown>(form);
            ComboBox devices = Find<ComboBox>(form);
            CheckBox startup = Find<CheckBox>(form);
            Control slider = FindByTypeName(form, "VolumeSlider");
            PropertyInfo sliderValue = slider.GetType().GetProperty("Value");
            Button save = form.AcceptButton as Button;
            Check(save != null && save.Text == "Save", "New settings window defaults to English");
            Check(numeric.Value == 83.7M && (int)sliderValue.GetValue(slider, null) == 837, "Existing fractional microphone target is loaded");
            Check(numeric.Text.Contains("."), "English numeric input uses a decimal point");
            SaveRendering(form, "settings-english.png");
            numeric.Value = 37.4M;
            if (devices.Items.Count > 1) devices.SelectedIndex = 1;
            numeric.Value = 37.4M;
            string unsavedDevice = ((AudioDeviceInfo)devices.SelectedItem).Id;
            startup.Checked = !startup.Checked;
            bool unsavedStartup = startup.Checked;
            Ui.SetLanguage(AppLanguage.Russian);
            Application.DoEvents();
            Check(save.Text == "Сохранить", "An already-open settings window switches live to Russian");
            Check(numeric.Text.Contains(","), "Russian numeric input uses a decimal comma");
            Check(numeric.Value == 37.4M && (int)sliderValue.GetValue(slider, null) == 374, "Language switch preserves unsaved fractional level");
            Check(((AudioDeviceInfo)devices.SelectedItem).Id == unsavedDevice && startup.Checked == unsavedStartup, "Language switch preserves unsaved device and startup choice");
            Check(saves == 0, "Language switch does not invoke microphone/startup save callback");
            SaveRendering(form, "settings-russian.png");
            SetPendingNumericText(numeric, "41,2");
            Ui.SetLanguage(AppLanguage.English);
            Application.DoEvents();
            Check(numeric.Value == 41.2M && numeric.Text.Contains("."), "An in-progress Russian decimal edit survives switching to English");
            SetPendingNumericText(numeric, "56.7");
            Ui.SetLanguage(AppLanguage.Russian);
            Application.DoEvents();
            Check(numeric.Value == 56.7M && numeric.Text.Contains(","), "An in-progress English decimal edit survives switching to Russian");
            SetPendingNumericText(numeric, "56,7");
            Ui.SetLanguage(AppLanguage.English);
            Application.DoEvents();
            Check(numeric.Value == 56.7M && numeric.Text.Contains("."), "A pending edit equal to the current value is not reparsed using the new culture");
            SetPendingNumericText(numeric, "invalid text");
            Ui.SetLanguage(AppLanguage.Russian);
            Application.DoEvents();
            Check(numeric.Value == 56.7M && numeric.Text.Contains(","), "Invalid pending input keeps the last valid level when the language changes");
            sliderValue.SetValue(slider, 123, null);
            Check(numeric.Value == 12.3M, "Russian slider retains tenth-percent precision");
            Ui.SetLanguage(AppLanguage.English);
            Application.DoEvents();
            Check(save.Text == "Save" && numeric.Value == 12.3M && numeric.Text.Contains("."), "Switching back to English preserves edited value and updates formatting");
            slider.AccessibilityObject.Value = "45.6%";
            Check(numeric.Value == 45.6M, "English accessible slider value uses the selected language culture");
            Ui.SetLanguage(AppLanguage.Russian);
            slider.AccessibilityObject.Value = "78,9%";
            Check(numeric.Value == 78.9M, "Russian accessible slider value uses the selected language culture");
            Ui.SetLanguage(AppLanguage.English);
            numeric.Value = 83.7M;
            save.PerformClick();
            Application.DoEvents();
            Check(saves == 1 && captured.DeviceId == unsavedDevice && Math.Abs(captured.Level - 0.837f) < 0.00001f && capturedStartup == unsavedStartup, "Stub save receives the exact unsaved device, level and startup choice");
        }
        Ui.SetLanguage(AppLanguage.Russian);
        Check(saves == 1, "Disposed settings window does not react to a subsequent language change");
        Check(!File.Exists(Storage.SettingsPath) && !File.Exists(Storage.StatusPath), "UI checks do not persist microphone settings or run the guard");
    }
    private static void SetPendingNumericText(NumericUpDown control, string text)
    {
        TextBox edit = Find<TextBox>(control);
        if (edit == null) throw new Exception("Numeric edit box not found.");
        edit.Text = text;
        PropertyInfo userEdit = typeof(UpDownBase).GetProperty("UserEdit", BindingFlags.NonPublic | BindingFlags.Instance);
        userEdit.SetValue(control, true, null);
        Check((bool)userEdit.GetValue(control, null), "Numeric input is pending user text rather than a committed Value");
    }
}
