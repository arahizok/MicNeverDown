using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MicNeverDown
{
    public sealed class TrayApp : ApplicationContext
    {
        private readonly GuardEngine engine;
        private readonly NotifyIcon tray;
        private readonly Icon catIcon;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem pauseItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem settingsItem;
        private readonly ToolStripMenuItem languageItem;
        private readonly ToolStripMenuItem englishItem;
        private readonly ToolStripMenuItem russianItem;
        private readonly ToolStripMenuItem disableStartupExitItem;
        private readonly ToolStripMenuItem exitItem;
        private readonly System.Windows.Forms.Timer timer;
        private readonly EventWaitHandle stopEvent;
        private readonly EventWaitHandle showEvent;
        private readonly EventWaitHandle pauseEvent;
        private readonly EventWaitHandle resumeEvent;
        private SettingsForm settingsForm;
        private DateTime nextStatus = DateTime.MinValue;
        private DateTime nextHeartbeat = DateTime.MinValue;
        private string lastStatus;
        private bool exiting;

        public TrayApp(bool showSettings)
        {
            engine = new GuardEngine(new CoreAudioProvider());
            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess()) engine.Status.ProcessId = process.Id;
            try { engine.Configure(Storage.Read<Settings>(Storage.SettingsPath)); }
            catch (FileNotFoundException) { showSettings = true; }
            catch (DirectoryNotFoundException) { showSettings = true; }
            catch (Exception error) { Storage.WriteError(error); showSettings = true; }
            stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.StopEventName);
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            pauseEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.PauseEventName);
            resumeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ResumeEventName);
            using (Stream resource = typeof(Program).Assembly.GetManifestResourceStream("MicNeverDown.Cat.ico"))
            using (Icon original = new Icon(resource, SystemInformation.SmallIconSize)) catIcon = (Icon)original.Clone();
            tray = new NotifyIcon { Icon = catIcon, Text = "MicNeverDown", Visible = true };
            menu = new ContextMenuStrip();
            settingsItem = new ToolStripMenuItem("", null, delegate { ShowSettings(); });
            menu.Items.Add(settingsItem);
            pauseItem = new ToolStripMenuItem("", null, delegate
            {
                engine.Paused = !engine.Paused;
                Tick();
            });
            menu.Items.Add(pauseItem);
            startupItem = new ToolStripMenuItem();
            startupItem.Click += delegate
            {
                try { Startup.Set(!Startup.Enabled); startupItem.Checked = Startup.Enabled; }
                catch (Exception error) { ShowError(error); }
            };
            menu.Items.Add(startupItem);
            languageItem = new ToolStripMenuItem();
            englishItem = new ToolStripMenuItem("English", null, delegate { ChangeLanguage(AppLanguage.English); });
            russianItem = new ToolStripMenuItem("Русский", null, delegate { ChangeLanguage(AppLanguage.Russian); });
            languageItem.DropDownItems.Add(englishItem);
            languageItem.DropDownItems.Add(russianItem);
            menu.Items.Add(languageItem);
            menu.Items.Add(new ToolStripSeparator());
            disableStartupExitItem = new ToolStripMenuItem("", null, delegate
            {
                try { Startup.Set(false); ExitThread(); }
                catch (Exception error) { ShowError(error); }
            });
            menu.Items.Add(disableStartupExitItem);
            exitItem = new ToolStripMenuItem("", null, delegate { ExitThread(); });
            menu.Items.Add(exitItem);
            menu.Opening += delegate { startupItem.Checked = Startup.Enabled; };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            timer = new System.Windows.Forms.Timer { Interval = 150 };
            Ui.LanguageChanged += LanguageChanged;
            ApplyLanguage();
            timer.Tick += delegate
            {
                if (stopEvent.WaitOne(0)) { ExitThread(); return; }
                if (pauseEvent.WaitOne(0)) engine.Paused = true;
                if (resumeEvent.WaitOne(0)) engine.Paused = false;
                if (showEvent.WaitOne(0)) ShowSettings();
                Tick();
            };
            Tick();
            timer.Start();
            if (showSettings) ShowSettings();
        }

        private void Tick()
        {
            DateTime now = DateTime.UtcNow;
            engine.Poll(now);
            GuardStatus status = engine.Status;
            pauseItem.Text = engine.Paused ? Ui.Text("Resume", "Возобновить") : Ui.Text("Pause", "Приостановить");
            pauseItem.Checked = engine.Paused;
            string tooltip = status.State == "Locked" ? Ui.Text("MicNeverDown: microphone ", "MicNeverDown: микрофон ") + Math.Round(status.TargetLevel * 100).ToString(Ui.Culture) + "%" :
                status.State == "Paused" ? Ui.Text("MicNeverDown: paused", "MicNeverDown: пауза") : status.State == "Unconfigured" ? Ui.Text("MicNeverDown: select a microphone", "MicNeverDown: выберите микрофон") : Ui.Text("MicNeverDown: waiting for microphone", "MicNeverDown: ожидание микрофона");
            if (tray.Text != tooltip) tray.Text = tooltip;
            string signature = status.State + "|" + status.DeviceId + "|" + status.TargetLevel + "|" + status.Corrections + "|" + status.Muted + "|" + status.Error;
            if (now >= nextStatus && (signature != lastStatus || now >= nextHeartbeat))
            {
                WriteStatus();
                lastStatus = signature;
                nextStatus = now.AddSeconds(1);
                nextHeartbeat = now.AddMinutes(1);
            }
        }

        private void WriteStatus()
        {
            try { Storage.Write(Storage.StatusPath, engine.Status); }
            catch (Exception error) { Storage.WriteError(error); }
        }

        private void ChangeLanguage(AppLanguage language)
        {
            try { Ui.SetLanguage(language); }
            catch (Exception error) { ShowError(error); }
        }

        private void LanguageChanged(object sender, EventArgs args) { ApplyLanguage(); }

        private void ApplyLanguage()
        {
            settingsItem.Text = Ui.Text("Microphone and volume…", "Микрофон и уровень…");
            startupItem.Text = Ui.Text("Start with Windows", "Запускать вместе с Windows");
            languageItem.Text = Ui.Text("Language", "Язык");
            englishItem.Checked = Ui.CurrentLanguage == AppLanguage.English;
            russianItem.Checked = Ui.CurrentLanguage == AppLanguage.Russian;
            disableStartupExitItem.Text = Ui.Text("Disable startup and exit", "Отключить автозапуск и выйти");
            exitItem.Text = Ui.Text("Exit", "Выйти");
            Tick();
        }

        private void ShowSettings()
        {
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Show(); settingsForm.Activate(); return;
            }
            settingsForm = new SettingsForm(engine.Configuration, SaveSettings) { Icon = catIcon };
            settingsForm.FormClosed += delegate { settingsForm = null; };
            settingsForm.Show(); settingsForm.Activate();
        }

        private void SaveSettings(Settings value, bool enableStartup)
        {
            Storage.Validate(value);
            Settings previous = engine.Configuration;
            Storage.Write(Storage.SettingsPath, value);
            try { Startup.Set(enableStartup); }
            catch (Exception startupError)
            {
                try
                {
                    if (previous == null) File.Delete(Storage.SettingsPath);
                    else Storage.Write(Storage.SettingsPath, previous);
                }
                catch (Exception rollbackError)
                {
                    Storage.WriteError(rollbackError);
                    throw new IOException(Ui.Text("Could not change startup or restore the previous settings file. Check access to the application folder.", "Не удалось изменить автозапуск и восстановить прежний файл настроек. Проверьте доступ к папке программы."),
                        new AggregateException(startupError, rollbackError));
                }
                throw;
            }
            engine.Configure(value);
            startupItem.Checked = Startup.Enabled;
            lastStatus = null;
            nextStatus = DateTime.MinValue;
            Tick();
        }

        private static void ShowError(Exception error)
        {
            Storage.WriteError(error);
            MessageBox.Show(error.Message, "MicNeverDown", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void ExitThreadCore()
        {
            if (exiting) return;
            exiting = true;
            timer.Stop();
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close();
            engine.Dispose();
            WriteStatus();
            tray.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Ui.LanguageChanged -= LanguageChanged;
                engine.Dispose();
                timer.Dispose();
                tray.Dispose();
                menu.Dispose();
                catIcon.Dispose();
                stopEvent.Dispose(); showEvent.Dispose(); pauseEvent.Dispose(); resumeEvent.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
