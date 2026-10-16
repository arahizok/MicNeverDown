using System;
using System.Globalization;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace MicNeverDown
{
    public static class Program
    {
        private static readonly string Prefix = @"Local\MicNeverDown-" + WindowsIdentity.GetCurrent().User.Value;
        public static readonly string StopEventName = Prefix + "-Stop";
        public static readonly string ShowEventName = Prefix + "-Show";
        public static readonly string PauseEventName = Prefix + "-Pause";
        public static readonly string ResumeEventName = Prefix + "-Resume";

        private static bool Signal(string name)
        {
            try { using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name)) handle.Set(); return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        private static bool Acquire(Mutex mutex)
        {
            try { return mutex.WaitOne(0); }
            catch (AbandonedMutexException) { return true; }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Ui.Initialize(); return Run(args); }
            catch (Exception error)
            {
                Storage.WriteError(error);
                if (args.Length == 0 || (args.Length == 1 && args[0] == "--settings"))
                    MessageBox.Show(error.Message, "MicNeverDown", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 2 && args[0] == "--list")
            {
                Storage.Write(args[1], AudioDevices.ListCapture());
                return 0;
            }
            if (args.Length == 3 && args[0] == "--configure")
            {
                using (Mutex mutex = new Mutex(false, Prefix))
                {
                    if (!Acquire(mutex)) throw new InvalidOperationException(Ui.Text("Close the running application first.", "Сначала завершите работающую программу."));
                    try
                    {
                        AudioDeviceInfo device = AudioDevices.ListCapture().Find(delegate(AudioDeviceInfo item) { return item.Id == args[1]; });
                        if (device == null) throw new InvalidOperationException(Ui.Text("Microphone not found.", "Микрофон не найден."));
                        Settings value = new Settings
                        {
                            DeviceId = device.Id, DeviceName = device.Name,
                            Level = Single.Parse(args[2], CultureInfo.InvariantCulture)
                        };
                        Storage.Validate(value);
                        Storage.Write(Storage.SettingsPath, value);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--enable-startup") { Startup.Set(true); return 0; }
            if ((args.Length == 1 || (args.Length == 2 && args[1] == "--stop")) && args[0] == "--disable-startup")
            {
                Startup.Set(false);
                if (args.Length == 2) Signal(StopEventName);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--stop") { Signal(StopEventName); return 0; }
            if (args.Length == 1 && args[0] == "--pause") { Signal(PauseEventName); return 0; }
            if (args.Length == 1 && args[0] == "--resume") { Signal(ResumeEventName); return 0; }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--settings" && args[0] != "--background"))
                throw new ArgumentException(Ui.Text("Unknown command-line arguments.", "Неизвестные параметры запуска."));
            using (Mutex mutex = new Mutex(false, Prefix))
            {
                if (!Acquire(mutex))
                {
                    if (args.Length == 0 || args[0] != "--background") Signal(ShowEventName);
                    return 0;
                }
                try
                {
                    bool show = args.Length == 0 || args[0] == "--settings";
                    using (TrayApp application = new TrayApp(show)) Application.Run(application);
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
